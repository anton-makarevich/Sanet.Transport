using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Sanet.Transport.SignalR.Client.Relay;

namespace Sanet.Transport.SignalR.Client.Publishers;

/// <summary>
/// Relay-specific implementation of <see cref="ITransportPublisher"/> using SignalR.
/// Connects outbound to a cloud RelayHub using WebSockets and room session token authentication.
/// </summary>
public class RelayClientPublisher : ITransportPublisher, IAsyncDisposable
{
    private readonly HubConnection _hubConnection;
    private readonly string _roomCode;
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
    public RelayClientPublisher(string hubUrl, string roomCode, string sessionToken)
    {
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

        var uriBuilder = new UriBuilder(hubUrl);
        var queryToAppend = $"sessionToken={Uri.EscapeDataString(sessionToken)}";
        if (string.IsNullOrEmpty(uriBuilder.Query) || uriBuilder.Query == "?")
        {
            uriBuilder.Query = queryToAppend;
        }
        else
        {
            uriBuilder.Query = uriBuilder.Query.TrimStart('?') + "&" + queryToAppend;
        }

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(uriBuilder.Uri, options =>
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
        catch (JsonException)
        {
            // Ignore malformed payloads
        }
    }

    private void HandleHubError(HubError error)
    {
        if (_isDisposed)
        {
            return;
        }

        HubErrorReceived?.Invoke(error);
    }

    private void HandlePeerConnected(string peerId)
    {
        if (_isDisposed)
        {
            return;
        }

        PeerConnected?.Invoke(peerId);
    }

    private void HandlePeerDisconnected(string peerId)
    {
        if (_isDisposed)
        {
            return;
        }

        PeerDisconnected?.Invoke(peerId);
    }

    private void NotifySubscribers(TransportMessage message)
    {
        Action<TransportMessage>[] currentSubscribers;
        lock (_subscribers)
        {
            currentSubscribers = _subscribers.ToArray();
        }

        foreach (var subscriber in currentSubscribers)
        {
            try
            {
                subscriber(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error notifying subscriber: {ex}");
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

        if (_hubConnection.State != HubConnectionState.Disconnected)
        {
            await _hubConnection.StopAsync();
        }

        await _hubConnection.DisposeAsync();

        GC.SuppressFinalize(this);
    }
}
