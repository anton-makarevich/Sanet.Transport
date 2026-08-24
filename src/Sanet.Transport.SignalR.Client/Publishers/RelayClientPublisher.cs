using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Sanet.Transport.SignalR.Client.Relay;

namespace Sanet.Transport.SignalR.Client.Publishers;

/// <summary>
/// Relay-specific implementation of <see cref="ITransportPublisher"/> using SignalR.
/// Connects outbound to a cloud RelayHub using WebSockets and short-lived relay-ticket
/// authentication. The relay ticket is bound into the connection URL at construction
/// time. When <paramref name="relayTicketExpiresAt"/> is supplied, automatic reconnect is
/// configured with a retry window that ends before the ticket expires, so repeatable
/// unexpired tickets are reused after transient transport failures.
/// When <paramref name="ticketRefresh"/> is supplied, a closed connection is not terminal:
/// the delegate is invoked to obtain a fresh relay ticket, the underlying
/// <see cref="HubConnection"/> is rebuilt around it (preserving subscribers and public
/// events) and restarted. The public <see cref="Closed"/> event then only fires when no
/// refresh delegate is configured, the delegate fails or returns null, or the bounded
/// restart attempts are exhausted. After a successful manual rebuild the public
/// <see cref="Reconnected"/> event is raised explicitly.
/// Subscriber callbacks and public events are dispatched via the <see cref="SynchronizationContext"/>
/// active at construction time, if any. Consumers on UI frameworks (Avalonia, WPF, WinUI) should
/// construct this publisher on the UI thread to receive callbacks without manual marshaling.
/// </summary>
public class RelayClientPublisher : ITransportPublisher
{
    private const int MaxRestartAttempts = 3;
    private const int DefaultOutboundQueueCapacity = 500;

    private readonly string _hubUrl;
    private readonly string _roomCode;
    private readonly ILogger<RelayClientPublisher> _logger;
    private readonly SynchronizationContext? _syncContext;
    private readonly List<Action<TransportMessage>> _subscribers = [];
    private readonly Func<CancellationToken, Task<RelayTicketRefresh?>>? _ticketRefresh;
    private readonly int _outboundQueueCapacity;
    private readonly Queue<TransportMessage> _outboundQueue = new();
    private readonly object _connectionLock = new();
    private readonly CancellationTokenSource _rebuildCts = new();
    private HubConnection _hubConnection;
    private DateTimeOffset? _relayTicketExpiresAt;
    private long _sequenceNumber;
    private volatile bool _isDisposed;
    private bool _isRebuilding;

    /// <summary>
    /// Event raised when a peer connects to the room.
    /// </summary>
    public event Action<string>? PeerConnected;

    /// <summary>
    /// Event raised when a peer disconnects from the room.
    /// </summary>
    public event Action<string>? PeerDisconnected;

    /// <summary>
    /// Event raised when a hub error is received.
    /// </summary>
    public event Action<HubError>? HubErrorReceived;

    /// <summary>
    /// Event raised when the host disconnects from the room.
    /// </summary>
    public event Action? HostDisconnected;

    /// <summary>
    /// Event raised when the connection is attempting to reconnect.
    /// </summary>
    public event Action<Exception?>? Reconnecting;

    /// <summary>
    /// Event raised when the connection has been reestablished, either by automatic
    /// reconnect within a ticket window or by a successful manual rebuild with a
    /// freshly issued relay ticket.
    /// </summary>
    public event Action<string?>? Reconnected;

    /// <summary>
    /// Event raised when the connection has been closed terminally. This fires when no
    /// ticket-refresh delegate is configured, when the delegate fails or returns null,
    /// when the bounded restart attempts are exhausted, or when the publisher is disposed.
    /// Once closed, callers must obtain a fresh relay ticket and recreate this publisher.
    /// </summary>
    public event Action<Exception?>? Closed;

    /// <summary>
    /// Gets the current state of the underlying SignalR connection.
    /// </summary>
    public HubConnectionState State => CurrentConnection.State;

    /// <summary>
    /// Gets whether the publisher is currently connected to the hub.
    /// </summary>
    public bool IsConnected => CurrentConnection.State == HubConnectionState.Connected;

    /// <summary>
    /// Creates a new instance of <see cref="RelayClientPublisher"/>.
    /// </summary>
    /// <param name="hubUrl">The base URL of the SignalR relay hub.</param>
    /// <param name="roomCode">The 6-character room code.</param>
    /// <param name="relayTicket">The short-lived relay ticket issued by the REST relay-ticket API.</param>
    /// <param name="logger">Logger</param>
    /// <param name="relayTicketExpiresAt">
    /// When provided, enables automatic reconnect with a retry window that ends before this
    /// expiry, reusing the repeatable ticket after transient transport failures. When null,
    /// no automatic reconnect is configured.
    /// </param>
    /// <param name="ticketRefresh">
    /// When provided, invoked after the connection closes to obtain a fresh relay ticket
    /// and transparently rebuild and restart the underlying connection. Return null or throw
    /// to make the close terminal (raising <see cref="Closed"/>). The cancellation token is
    /// canceled when the publisher is disposed.
    /// </param>
    /// <param name="outboundQueueCapacity">
    /// The maximum number of messages queued while reconnecting or rebuilding. When full,
    /// <see cref="PublishMessage"/> throws <see cref="TransportPublishException"/> with
    /// <see cref="PublishFailureReason.QueueFull"/>.
    /// </param>
    public RelayClientPublisher(
        string hubUrl,
        string roomCode,
        string relayTicket,
        ILogger<RelayClientPublisher> logger,
        DateTimeOffset? relayTicketExpiresAt = null,
        Func<CancellationToken, Task<RelayTicketRefresh?>>? ticketRefresh = null,
        int outboundQueueCapacity = DefaultOutboundQueueCapacity)
    {
        _logger = logger;
        _syncContext = SynchronizationContext.Current;

        if (string.IsNullOrWhiteSpace(hubUrl))
        {
            throw new ArgumentException("Hub URL cannot be null or empty", nameof(hubUrl));
        }

        if (roomCode is null || roomCode.Length != 6)
        {
            throw new ArgumentException("Room code must be exactly 6 characters", nameof(roomCode));
        }

        if (string.IsNullOrWhiteSpace(relayTicket))
        {
            throw new ArgumentException("Relay ticket cannot be null or empty", nameof(relayTicket));
        }

        _hubUrl = hubUrl;
        _roomCode = roomCode;
        _relayTicketExpiresAt = relayTicketExpiresAt;
        _ticketRefresh = ticketRefresh;
        if (outboundQueueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outboundQueueCapacity),
                "Outbound queue capacity must be greater than zero.");
        }

        _outboundQueueCapacity = outboundQueueCapacity;

        _hubConnection = BuildConnection(hubUrl, relayTicket, relayTicketExpiresAt);
        AttachHandlers(_hubConnection);
    }

    /// <summary>
    /// Builds the SignalR hub connection URL, appending the relay ticket as a query-string
    /// parameter and replacing any ticket parameter already present in the hub URL.
    /// </summary>
    /// <param name="hubUrl">The base URL of the SignalR relay hub.</param>
    /// <param name="relayTicket">The short-lived relay ticket issued by the REST relay-ticket API.</param>
    internal static string BuildConnectionUrl(string hubUrl, string relayTicket)
    {
        var uriBuilder = new UriBuilder(hubUrl);
        var queryToAppend = $"ticket={Uri.EscapeDataString(relayTicket)}";

        if (string.IsNullOrEmpty(uriBuilder.Query) || uriBuilder.Query == "?")
        {
            uriBuilder.Query = queryToAppend;
        }
        else
        {
            var existingQueryParameters = uriBuilder.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(pair => !pair
                    .Split('=', 2)[0]
                    .Equals("ticket", StringComparison.OrdinalIgnoreCase));

            uriBuilder.Query = string.Join('&', existingQueryParameters.Append(queryToAppend));
        }

        return uriBuilder.Uri.AbsoluteUri;
    }

    /// <summary>
    /// Starts the connection to the SignalR relay hub.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(RelayClientPublisher));
        }

        var connection = CurrentConnection;
        if (connection.State == HubConnectionState.Disconnected)
        {
            await connection.StartAsync();
        }
    }

    /// <summary>
    /// Publishes a transport message to the relay hub. While reconnecting or rebuilding
    /// the connection, the message is queued (bounded by <paramref name="outboundQueueCapacity"/>
    /// of the constructor) and flushed in order once the connection is reestablished;
    /// when the queue is full a <see cref="TransportPublishException"/> with
    /// <see cref="PublishFailureReason.QueueFull"/> is thrown. When disconnected with no
    /// rebuild in progress, a <see cref="TransportPublishException"/> with
    /// <see cref="PublishFailureReason.NotConnected"/> is thrown.
    /// </summary>
    /// <param name="message">The transport message to publish.</param>
    public async Task PublishMessage(TransportMessage message)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(RelayClientPublisher));
        }

        var connection = CurrentConnection;
        if (connection.State == HubConnectionState.Connected)
        {
            await SendEnvelopeAsync(connection, message);
            return;
        }

        lock (_connectionLock)
        {
            if (_isRebuilding || connection.State == HubConnectionState.Reconnecting)
            {
                if (_outboundQueue.Count >= _outboundQueueCapacity)
                {
                    _logger.LogWarning(
                        "Message rejected: outbound queue is full ({Capacity} messages)",
                        _outboundQueueCapacity);
                    throw new TransportPublishException(
                        PublishFailureReason.QueueFull,
                        $"Outbound queue is full ({_outboundQueueCapacity} messages).");
                }

                _outboundQueue.Enqueue(message);
                return;
            }
        }

        _logger.LogWarning("Message rejected: relay client is not connected");
        throw new TransportPublishException(
            PublishFailureReason.NotConnected,
            "Relay client is not connected.");
    }

    private async Task SendEnvelopeAsync(HubConnection connection, TransportMessage message)
    {
        var serializedPayload = JsonSerializer.Serialize(message);
        var envelope = new RelayEnvelope(
            SenderId: connection.ConnectionId ?? string.Empty,
            Payload: serializedPayload,
            SchemaVersion: "1.0.0",
            SequenceNumber: Interlocked.Increment(ref _sequenceNumber),
            Timestamp: DateTime.UtcNow);

        await connection.InvokeAsync("Relay", _roomCode, envelope);
    }

    /// <summary>
    /// Drains the outbound queue after a successful rebuild, sending each drained message
    /// outside the lock so concurrent publishes cannot interleave; mid-flush publishes are
    /// appended to the back of the queue and picked up by this loop.
    /// </summary>
    private async Task FlushOutboundQueueAsync(HubConnection connection)
    {
        while (!_isDisposed)
        {
            TransportMessage? message;
            lock (_connectionLock)
            {
                message = _outboundQueue.Count > 0 ? _outboundQueue.Dequeue() : null;
            }

            if (message is null)
            {
                return;
            }

            try
            {
                await SendEnvelopeAsync(connection, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush queued message after rebuild; dropping it");
            }
        }
    }

    /// <summary>
    /// Subscribes to receive transport messages from the relay.
    /// </summary>
    /// <param name="onMessageReceived">Action called when a message is received.</param>
    public void Subscribe(Action<TransportMessage> onMessageReceived)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(RelayClientPublisher));
        }

        ArgumentNullException.ThrowIfNull(onMessageReceived);

        lock (_subscribers)
        {
            _subscribers.Add(onMessageReceived);
        }
    }

    private HubConnection CurrentConnection
    {
        get
        {
            lock (_connectionLock)
            {
                return _hubConnection;
            }
        }
    }

    private HubConnection BuildConnection(string hubUrl, string relayTicket, DateTimeOffset? expiresAt)
    {
        var builder = new HubConnectionBuilder()
            .WithUrl(BuildConnectionUrl(hubUrl, relayTicket), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
            });

        if (expiresAt is { } ticketExpiry)
        {
            builder = builder.WithAutomaticReconnect(new RelayTicketExpiryRetryPolicy(ticketExpiry));
        }

        return builder.Build();
    }

    private void AttachHandlers(HubConnection connection)
    {
        connection.On<RelayEnvelope>("OnReceive", HandleEnvelopeReceived);
        connection.On<HubError>("OnError", HandleHubError);
        connection.On<string>("OnPeerConnected", HandlePeerConnected);
        connection.On<string>("OnPeerDisconnected", HandlePeerDisconnected);

        connection.Reconnecting += OnReconnecting;
        connection.Reconnected += OnReconnected;
        connection.Closed += OnClosed;
    }

    private void DetachHandlers(HubConnection connection)
    {
        connection.Reconnecting -= OnReconnecting;
        connection.Reconnected -= OnReconnected;
        connection.Closed -= OnClosed;
    }

    private void HandleEnvelopeReceived(RelayEnvelope envelope)
    {
        if (_isDisposed || string.IsNullOrEmpty(envelope.Payload))
        {
            return;
        }

        try
        {
            var message = JsonSerializer.Deserialize<TransportMessage>(envelope.Payload);
            if (message is null)
            {
                return;
            }

            NotifySubscribers(message);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Malformed payload received in envelope from {SenderId}", envelope.SenderId);
        }
    }

    private void HandleHubError(HubError error)
    {
        if (_isDisposed)
        {
            return;
        }

        if (error.Code == HubErrorCode.HostDisconnected)
        {
            _logger.LogWarning("Host disconnected: {Message}", error.Message);
            RaiseEvent(HostDisconnected);
            return;
        }

        RaiseEvent(() => HubErrorReceived?.Invoke(error));
    }

    private void HandlePeerConnected(string peerId)
    {
        if (_isDisposed)
        {
            return;
        }

        RaiseEvent(() => PeerConnected?.Invoke(peerId));
    }

    private void HandlePeerDisconnected(string peerId)
    {
        if (_isDisposed)
        {
            return;
        }

        RaiseEvent(() => PeerDisconnected?.Invoke(peerId));
    }

    private void RaiseEvent(Action? handler)
    {
        if (handler is null) return;
        if (_syncContext is not null)
            _syncContext.Post(_ => handler(), null);
        else
            handler();
    }

    private void NotifySubscribers(TransportMessage message)
    {
        Action<TransportMessage>[] currentSubscribers;
        lock (_subscribers)
        {
            currentSubscribers = _subscribers.ToArray();
        }

        if (_syncContext is not null)
        {
            _syncContext.Post(_ =>
            {
                foreach (var subscriber in currentSubscribers)
                {
                    try
                    {
                        subscriber(message);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error notifying subscriber via sync context");
                    }
                }
            }, null);
        }
        else
        {
            foreach (var subscriber in currentSubscribers)
            {
                try
                {
                    subscriber(message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error notifying subscriber");
                }
            }
        }
    }

    /// <summary>
    /// Asynchronously disposes the publisher and closes the hub connection.
    /// Any in-flight ticket-refresh rebuild is canceled.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _rebuildCts.Cancel();

        var connection = CurrentConnection;
        DetachHandlers(connection);

        if (connection.State != HubConnectionState.Disconnected)
        {
            await connection.StopAsync();
        }

        await connection.DisposeAsync();
        _rebuildCts.Dispose();

        GC.SuppressFinalize(this);
    }

    private Task OnReconnecting(Exception? exception)
    {
        _logger.LogInformation(exception, "Relay client reconnecting");
        RaiseEvent(() => Reconnecting?.Invoke(exception));
        return Task.CompletedTask;
    }

    private Task OnReconnected(string? connectionId)
    {
        _logger.LogInformation("Relay client reconnected with connection ID {ConnectionId}", connectionId);

        RaiseEvent(() => Reconnected?.Invoke(connectionId));
        return Task.CompletedTask;
    }

    private Task OnClosed(Exception? exception)
    {
        if (_isDisposed)
        {
            return Task.CompletedTask;
        }

        if (exception is not null)
        {
            _logger.LogError(exception, "Relay client connection closed with error");
        }
        else
        {
            _logger.LogInformation("Relay client connection closed");
        }

        if (_ticketRefresh is null)
        {
            RaiseTerminalClosed(exception);
            return Task.CompletedTask;
        }

        // Fire-and-forget: the SignalR Closed handler must return promptly.
        _ = Task.Run(() => RebuildConnectionAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Obtains a fresh relay ticket via the refresh delegate, rebuilds the underlying
    /// <see cref="HubConnection"/> around it and restarts it. Single-flight: overlapping
    /// close notifications while a rebuild runs are ignored. Raises the terminal
    /// <see cref="Closed"/> event when the delegate fails, returns null, or the bounded
    /// restart attempts are exhausted.
    /// </summary>
    private async Task RebuildConnectionAsync()
    {
        lock (_connectionLock)
        {
            if (_isRebuilding || _isDisposed)
            {
                return;
            }

            _isRebuilding = true;
        }

        try
        {
            await RefreshAndRestartAsync();
        }
        finally
        {
            lock (_connectionLock)
            {
                _isRebuilding = false;
            }
        }
    }

    private async Task RefreshAndRestartAsync()
    {
        RelayTicketRefresh? refresh;
        try
        {
            refresh = await _ticketRefresh!(_rebuildCts.Token);
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Relay ticket refresh failed; closing the connection terminally");
            RaiseTerminalClosed(ex);
            return;
        }

        if (_isDisposed)
        {
            return;
        }

        if (refresh is null)
        {
            _logger.LogWarning(
                "Relay ticket refresh returned no ticket; closing the connection terminally");
            RaiseTerminalClosed(null);
            return;
        }

        var newConnection = BuildConnection(_hubUrl, refresh.RelayTicket, refresh.ExpiresAt);
        HubConnection oldConnection;
        lock (_connectionLock)
        {
            oldConnection = _hubConnection;
            DetachHandlers(oldConnection);
            _hubConnection = newConnection;
            _relayTicketExpiresAt = refresh.ExpiresAt;
        }

        AttachHandlers(newConnection);

        Exception? lastStartFailure = null;
        for (var attempt = 1; attempt <= MaxRestartAttempts && !_isDisposed; attempt++)
        {
            try
            {
                await newConnection.StartAsync(_rebuildCts.Token);
                lastStartFailure = null;
                break;
            }
            catch (OperationCanceledException) when (_isDisposed)
            {
                return;
            }
            catch (Exception ex)
            {
                lastStartFailure = ex;
                _logger.LogWarning(
                    ex,
                    "Failed to restart relay connection with fresh ticket (attempt {Attempt}/{MaxAttempts})",
                    attempt,
                    MaxRestartAttempts);
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), _rebuildCts.Token);
                }
                catch (OperationCanceledException) when (_isDisposed)
                {
                    return;
                }
            }
        }

        if (lastStartFailure is not null)
        {
            _logger.LogError(
                "Giving up restarting relay connection after {MaxAttempts} attempts",
                MaxRestartAttempts);
            RaiseTerminalClosed(lastStartFailure);
            return;
        }

        if (_isDisposed)
        {
            return;
        }

        _logger.LogInformation(
            "Relay client rebuilt its connection with a fresh relay ticket (connection ID {ConnectionId})",
            newConnection.ConnectionId);

        RaiseEvent(() => Reconnected?.Invoke(newConnection.ConnectionId));

        await FlushOutboundQueueAsync(newConnection);

        _ = Task.Run(async () =>
        {
            try
            {
                await oldConnection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing superseded relay connection");
            }
        });
    }

    private void RaiseTerminalClosed(Exception? exception)
    {
        // Terminal close contract: Closed fires when no refresh delegate is configured,
        // when the delegate fails or returns no ticket, when the bounded restart attempts
        // are exhausted, or when the publisher is disposed. Callers must obtain a fresh
        // relay ticket and recreate the publisher once Closed is raised.
        RaiseEvent(() => Closed?.Invoke(exception));
    }
}
