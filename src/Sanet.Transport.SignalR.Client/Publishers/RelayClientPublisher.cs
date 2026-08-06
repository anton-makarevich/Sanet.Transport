using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Sanet.Transport.SignalR.Client.Relay;

namespace Sanet.Transport.SignalR.Client.Publishers;

/// <summary>
/// Relay-specific implementation of <see cref="ITransportPublisher"/> using SignalR.
/// Connects outbound to a cloud RelayHub using WebSockets and room session token authentication.
/// Subscriber callbacks and public events are dispatched via the <see cref="SynchronizationContext"/>
/// active at construction time, if any. Consumers on UI frameworks (Avalonia, WPF, WinUI) should
/// construct this publisher on the UI thread to receive callbacks without manual marshaling.
/// </summary>
public class RelayClientPublisher : ITransportPublisher, IAsyncDisposable
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
    /// <param name="sessionToken">The session token issued by the REST room join/create API.</param>
    /// <param name="logger">Logger</param>
    /// <param name="apiKey">Optional API key appended as a query parameter. Required by hubs
    /// that enforce relay authentication (RelayAuthenticationMiddleware).</param>
    public RelayClientPublisher(
        string hubUrl,
        string roomCode,
        string sessionToken,
        ILogger<RelayClientPublisher> logger,
        string? apiKey = null)
    {
        _logger = logger;
        _syncContext = SynchronizationContext.Current;

        if (string.IsNullOrWhiteSpace(hubUrl))
        {
            throw new ArgumentException("Hub URL cannot be null or empty", nameof(hubUrl));
        }

        if (string.IsNullOrWhiteSpace(roomCode))
        {
            throw new ArgumentException("Room code cannot be null or empty", nameof(roomCode));
        }

        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            throw new ArgumentException("Session token cannot be null or empty", nameof(sessionToken));
        }

        _roomCode = roomCode;

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(BuildConnectionUrl(hubUrl, sessionToken, apiKey), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
            })
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<RelayEnvelope>("OnReceive", HandleEnvelopeReceived);
        _hubConnection.On<HubError>("OnError", HandleHubError);
        _hubConnection.On<string>("OnPeerConnected", HandlePeerConnected);
        _hubConnection.On<string>("OnPeerDisconnected", HandlePeerDisconnected);

        _hubConnection.Reconnecting += OnReconnecting;
        _hubConnection.Reconnected += OnReconnected;
        _hubConnection.Closed += OnClosed;
    }

    /// <summary>
    /// Builds the SignalR hub connection URL, appending the session token (required) and,
    /// when provided, the api key as query-string parameters.
    /// </summary>
    /// <param name="hubUrl">The base URL of the SignalR relay hub.</param>
    /// <param name="sessionToken">The session token issued by the REST room join/create API.</param>
    /// <param name="apiKey">Optional API key required by hubs with relay authentication enabled.</param>
    internal static string BuildConnectionUrl(string hubUrl, string sessionToken, string? apiKey)
    {
        var uriBuilder = new UriBuilder(hubUrl);
        var queryToAppend = $"sessionToken={Uri.EscapeDataString(sessionToken)}";

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            queryToAppend += $"&apiKey={Uri.EscapeDataString(apiKey)}";
        }

        if (string.IsNullOrEmpty(uriBuilder.Query) || uriBuilder.Query == "?")
        {
            uriBuilder.Query = queryToAppend;
        }
        else
        {
            uriBuilder.Query = uriBuilder.Query.TrimStart('?') + "&" + queryToAppend;
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

        // Return Task.CompletedTask to signal that reconnect policy is owned
        // by the caller via HubConnectionBuilder.WithAutomaticReconnect().
        // Without that configuration, Closed is terminal.
        RaiseEvent(() => Closed?.Invoke(exception));
        return Task.CompletedTask;
    }
}
