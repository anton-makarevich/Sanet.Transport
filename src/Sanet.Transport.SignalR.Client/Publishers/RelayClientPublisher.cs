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
    private const int MaxDrainRetries = 5;

    private readonly string _hubUrl;
    private readonly string _roomCode;
    private readonly ILogger<RelayClientPublisher> _logger;
    private readonly SynchronizationContext? _syncContext;
    private readonly List<Action<TransportMessage>> _subscribers = [];
    private readonly Func<CancellationToken, Task<RelayTicketRefresh?>>? _ticketRefresh;
    private readonly int _outboundQueueCapacity;
    private readonly Queue<TransportMessage> _outboundQueue = new();
    private readonly Lock _connectionLock = new();
    private readonly CancellationTokenSource _rebuildCts = new();
    private HubConnection _hubConnection;
    private long _sequenceNumber;
    private volatile bool _isDisposed;
    private bool _isRebuilding;
    private Task? _rebuildTask;
    private HubConnection? _pendingCloseConnection;
    private volatile bool _isDrainingRecovery;
    private int _terminalClosedRaised;
    private readonly Dictionary<HubConnection, Func<Exception?, Task>> _closedHandlers = new();

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

        HubConnection? sendTarget;
        lock (_connectionLock)
        {
            if (_isRebuilding || _isDrainingRecovery || _hubConnection.State == HubConnectionState.Reconnecting)
            {
                EnqueueOrThrowQueueFull(message);
                return;
            }

            sendTarget = _hubConnection.State == HubConnectionState.Connected
                ? _hubConnection
                : null;
        }

        if (sendTarget is not null)
        {
            await SendEnvelopeAsync(sendTarget, message);
            return;
        }

        _logger.LogWarning("Message rejected: relay client is not connected");
        throw new TransportPublishException(
            PublishFailureReason.NotConnected,
            "Relay client is not connected.");
    }

    private void EnqueueOrThrowQueueFull(TransportMessage message)
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
                _logger.LogError(
                    ex,
                    "Failed to flush queued message after rebuild; requeueing it for the next rebuild");
                lock (_connectionLock)
                {
                    // Requeue the failed message ahead of any messages enqueued mid-flush,
                    // preserving order, and stop so a subsequent rebuild retries everything.
                    var remaining = _outboundQueue.ToArray();
                    _outboundQueue.Clear();
                    _outboundQueue.Enqueue(message);
                    foreach (var queued in remaining)
                    {
                        _outboundQueue.Enqueue(queued);
                    }
                }
                return;
            }
        }
    }

    private async Task DrainQueueAndClearRebuildGate(HubConnection connection)
    {
        for (var attempt = 0; attempt <= MaxDrainRetries; attempt++)
        {
            await FlushOutboundQueueAsync(connection);

            lock (_connectionLock)
            {
                if (_outboundQueue.Count == 0 || _isDisposed || connection.State != HubConnectionState.Connected)
                {
                    TryScheduleFollowUpRebuild();
                    return;
                }

                if (attempt >= MaxDrainRetries)
                {
                    _logger.LogWarning(
                        "Drain queue exceeded maximum retry attempts ({MaxRetries}); clearing rebuild gate",
                        MaxDrainRetries);
                    TryScheduleFollowUpRebuild();
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)));
        }
    }

    private async Task DrainQueueAndClearRecoveryFlag(HubConnection connection)
    {
        for (var attempt = 0; attempt <= MaxDrainRetries; attempt++)
        {
            await FlushOutboundQueueAsync(connection);

            lock (_connectionLock)
            {
                if (_outboundQueue.Count == 0 || _isDisposed || connection.State != HubConnectionState.Connected)
                {
                    _isDrainingRecovery = false;
                    return;
                }

                if (attempt >= MaxDrainRetries)
                {
                    _logger.LogWarning(
                        "Recovery drain exceeded maximum retry attempts ({MaxRetries}); clearing recovery flag",
                        MaxDrainRetries);
                    _isDrainingRecovery = false;
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)));
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
        // Return a completed Task so the SignalR client awaits the handler before
        // dispatching the next envelope, preserving delivery order to subscribers.
        connection.On<RelayEnvelope>("OnReceive", envelope =>
        {
            HandleEnvelopeReceived(envelope);
            return Task.CompletedTask;
        });
        connection.On<HubError>("OnError", HandleHubError);
        connection.On<string>("OnPeerConnected", HandlePeerConnected);
        connection.On<string>("OnPeerDisconnected", HandlePeerDisconnected);

        connection.Reconnecting += OnReconnecting;
        connection.Reconnected += OnReconnected;

        // The Closed handler must know which connection closed so notifications from
        // superseded connections can be identified; the delegate is kept to allow detaching.
        var closedHandler = new Func<Exception?, Task>(exception => OnClosed(connection, exception));
        lock (_connectionLock)
        {
            _closedHandlers[connection] = closedHandler;
        }
        connection.Closed += closedHandler;
    }

    private void DetachHandlers(HubConnection connection)
    {
        connection.Reconnecting -= OnReconnecting;
        connection.Reconnected -= OnReconnected;

        Func<Exception?, Task>? closedHandler;
        lock (_connectionLock)
        {
            _closedHandlers.Remove(connection, out closedHandler);
        }

        if (closedHandler is not null)
        {
            connection.Closed -= closedHandler;
        }
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

        Task? rebuildTask;
        lock (_connectionLock)
        {
            rebuildTask = _rebuildTask;
        }

        if (rebuildTask is not null)
        {
            try
            {
                await rebuildTask;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "In-flight relay rebuild ended with an error during disposal");
            }
        }

        var connection = CurrentConnection;
        DetachHandlers(connection);

        if (connection.State != HubConnectionState.Disconnected)
        {
            await connection.StopAsync();
        }

        await connection.DisposeAsync();
        _rebuildCts.Dispose();

        RaiseTerminalClosed(null);

        GC.SuppressFinalize(this);
    }

    private Task OnReconnecting(Exception? exception)
    {
        _logger.LogInformation(exception, "Relay client reconnecting");
        RaiseEvent(() => Reconnecting?.Invoke(exception));
        return Task.CompletedTask;
    }

    private async Task OnReconnected(string? connectionId)
    {
        _logger.LogInformation("Relay client reconnected with connection ID {ConnectionId}", connectionId);

        var connection = CurrentConnection;

        // Block PublishMessage from sending directly while the queue is being drained
        // so messages published during the drain are appended in order.
        _isDrainingRecovery = true;
        try
        {
            // Raise the Reconnected event synchronously (not via RaiseEvent) so that
            // handlers execute while _isDrainingRecovery is still true.  This lets
            // handlers publish messages that will be queued and drained below; using
            // RaiseEvent would defer the handler via the SynchronizationContext,
            // causing it to run after the drain completes on an empty queue.
            Reconnected?.Invoke(connectionId);

            // Messages published while the connection was automatically reconnecting were
            // queued by PublishMessage; drain them now that the connection is restored.
            await FlushOutboundQueueAsync(connection);
        }
        finally
        {
            bool shouldDrain;
            lock (_connectionLock)
            {
                shouldDrain = _outboundQueue.Count > 0;
                if (!shouldDrain)
                {
                    _isDrainingRecovery = false;
                }
            }

            if (shouldDrain)
            {
                // Schedule a drain retry so queued messages are retried rather than
                // remaining stranded with _isDrainingRecovery still set.
                _ = Task.Run(() => DrainQueueAndClearRecoveryFlag(connection));
            }
        }
    }

    private Task OnClosed(HubConnection connection, Exception? exception)
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

        lock (_connectionLock)
        {
            // Ignore notifications from superseded connections whose handlers were
            // detached concurrently with this callback dispatch.
            if (!ReferenceEquals(connection, _hubConnection))
            {
                return Task.CompletedTask;
            }

            if (_isRebuilding)
            {
                // Retain the close notification of the current replacement connection so
                // a follow-up rebuild is scheduled when the active rebuild completes.
                _pendingCloseConnection = connection;
                return Task.CompletedTask;
            }

            _isRebuilding = true;
            _rebuildTask = Task.Run(RebuildConnectionAsync);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Obtains a fresh relay ticket via the refresh delegate, rebuilds the underlying
    /// <see cref="HubConnection"/> around it and restarts it. Single-flight: overlapping
    /// close notifications while a rebuild runs are retained (see <see cref="OnClosed"/>)
    /// and a follow-up rebuild is scheduled on completion if the current connection
    /// remains closed. Raises the terminal
    /// <see cref="Closed"/> event when the delegate fails, returns null, or the bounded
    /// restart attempts are exhausted.
    /// </summary>
    private async Task RebuildConnectionAsync()
    {
        try
        {
            await RefreshAndRestartAsync();
        }
        finally
        {
            TryScheduleFollowUpRebuild();
        }
    }

    private void TryScheduleFollowUpRebuild()
    {
        lock (_connectionLock)
        {
            _rebuildTask = null;
            var pendingConnection = _pendingCloseConnection;
            _pendingCloseConnection = null;

            if (_isDisposed ||
                pendingConnection is null ||
                !ReferenceEquals(pendingConnection, _hubConnection))
            {
                _isRebuilding = false;
                return;
            }

            if (pendingConnection.State == HubConnectionState.Connected)
            {
                if (_outboundQueue.Count > 0)
                {
                    _logger.LogInformation(
                        "Replacement relay connection is connected but queued messages remain; scheduling flush");
                    _rebuildTask = Task.Run(() => DrainQueueAndClearRebuildGate(pendingConnection));
                    return;
                }
                _isRebuilding = false;
                return;
            }

            _logger.LogInformation(
                "Replacement relay connection closed while the rebuild was completing; scheduling another rebuild");
            _rebuildTask = Task.Run(RebuildConnectionAsync);
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
        }

        try
        {
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
        }
        finally
        {
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
    }

    private void RaiseTerminalClosed(Exception? exception)
    {
        // Raise Closed at most once: disposal and a concurrent terminal rebuild
        // failure must not produce duplicate notifications.
        if (Interlocked.Exchange(ref _terminalClosedRaised, 1) != 0)
        {
            return;
        }

        // Terminal close contract: Closed fires when no refresh delegate is configured,
        // when the delegate fails or returns no ticket, when the bounded restart attempts
        // are exhausted, or when the publisher is disposed. Callers must obtain a fresh
        // relay ticket and recreate the publisher once Closed is raised.
        RaiseEvent(() => Closed?.Invoke(exception));
    }
}
