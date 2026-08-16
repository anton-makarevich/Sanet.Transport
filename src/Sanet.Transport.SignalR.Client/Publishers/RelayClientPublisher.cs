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
/// unexpired tickets are reused after transient transport failures; otherwise a
/// disconnect is terminal and callers must request a fresh ticket and recreate this
/// publisher.
/// Subscriber callbacks and public events are dispatched via the <see cref="SynchronizationContext"/>
/// active at construction time, if any. Consumers on UI frameworks (Avalonia, WPF, WinUI) should
/// construct this publisher on the UI thread to receive callbacks without manual marshaling.
/// </summary>
public class RelayClientPublisher : ITransportPublisher
{
    private readonly HubConnection _hubConnection;
    private readonly string _roomCode;
    private readonly ILogger<RelayClientPublisher> _logger;
    private readonly SynchronizationContext? _syncContext;
    private readonly List<Action<TransportMessage>> _subscribers = [];
    private long _sequenceNumber;
    private bool _isDisposed;

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
    /// Event raised when the connection has been reestablished.
    /// </summary>
    public event Action<string?>? Reconnected;

    /// <summary>
    /// Event raised when the connection has been closed.
    /// </summary>
    public event Action<Exception?>? Closed;

    /// <summary>
    /// Gets the current state of the underlying SignalR connection.
    /// </summary>
    public HubConnectionState State => _hubConnection.State;

    /// <summary>
    /// Gets whether the publisher is currently connected to the hub.
    /// </summary>
    public bool IsConnected => _hubConnection.State == HubConnectionState.Connected;

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
    public RelayClientPublisher(
        string hubUrl,
        string roomCode,
        string relayTicket,
        ILogger<RelayClientPublisher> logger,
        DateTimeOffset? relayTicketExpiresAt = null)
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

        _roomCode = roomCode;

        var builder = new HubConnectionBuilder()
            .WithUrl(BuildConnectionUrl(hubUrl, relayTicket), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
            });

        if (relayTicketExpiresAt is { } expiresAt)
        {
            builder = builder.WithAutomaticReconnect(new RelayTicketExpiryRetryPolicy(expiresAt));
        }

        _hubConnection = builder.Build();

        _hubConnection.On<RelayEnvelope>("OnReceive", HandleEnvelopeReceived);
        _hubConnection.On<HubError>("OnError", HandleHubError);
        _hubConnection.On<string>("OnPeerConnected", HandlePeerConnected);
        _hubConnection.On<string>("OnPeerDisconnected", HandlePeerDisconnected);

        _hubConnection.Reconnecting += OnReconnecting;
        _hubConnection.Reconnected += OnReconnected;
        _hubConnection.Closed += OnClosed;
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

        if (_hubConnection.State == HubConnectionState.Disconnected)
        {
            await _hubConnection.StartAsync();
        }
    }

    /// <summary>
    /// Publishes a transport message to the relay hub.
    /// </summary>
    /// <param name="message">The transport message to publish.</param>
    public async Task PublishMessage(TransportMessage message)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(RelayClientPublisher));
        }

        if (_hubConnection.State == HubConnectionState.Reconnecting)
        {
            _logger.LogError("Message dropped: client is reconnecting — no message queuing in v1");
            return;
        }

        if (_hubConnection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("Relay client is not connected.");
        }

        var serializedPayload = JsonSerializer.Serialize(message);
        var envelope = new RelayEnvelope(
            SenderId: _hubConnection.ConnectionId ?? string.Empty,
            Payload: serializedPayload,
            SchemaVersion: "1.0.0",
            SequenceNumber: Interlocked.Increment(ref _sequenceNumber),
            Timestamp: DateTime.UtcNow);

        await _hubConnection.InvokeAsync("Relay", _roomCode, envelope);
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
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _hubConnection.Reconnecting -= OnReconnecting;
        _hubConnection.Reconnected -= OnReconnected;
        _hubConnection.Closed -= OnClosed;

        if (_hubConnection.State != HubConnectionState.Disconnected)
        {
            await _hubConnection.StopAsync();
        }

        await _hubConnection.DisposeAsync();

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
        if (exception is not null)
        {
            _logger.LogError(exception, "Relay client connection closed with error");
        }
        else
        {
            _logger.LogInformation("Relay client connection closed");
        }

        // Closed is terminal: it fires when no retry window is configured (no ticket
        // expiry was supplied), when the retry window ends before the relay ticket
        // expires, or when the publisher is disposed. Reconnecting with an expired
        // ticket would fail authentication, so callers must obtain a fresh relay
        // ticket and recreate the publisher once Closed is raised.
        RaiseEvent(() => Closed?.Invoke(exception));
        return Task.CompletedTask;
    }
}
