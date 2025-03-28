using Microsoft.AspNetCore.SignalR.Client;

namespace Sanet.Transport.SignalR.Publishers;

/// <summary>
/// Client-side implementation of ITransportPublisher using SignalR
/// </summary>
public class SignalRClientPublisher : ITransportPublisher, IAsyncDisposable
{
    private readonly HubConnection _hubConnection;
    private readonly List<Action<TransportMessage>> _subscribers = [];
    private bool _isDisposed;

    /// <summary>
    /// Creates a new instance of SignalRClientPublisher
    /// </summary>
    /// <param name="hubUrl">The URL of the SignalR hub</param>
    public SignalRClientPublisher(string hubUrl)
    {
        if (string.IsNullOrEmpty(hubUrl))
        {
            throw new ArgumentException("Hub URL cannot be null or empty", nameof(hubUrl));
        }

        // Create the connection to the hub
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        // Register the message handler
        _hubConnection.On<TransportMessage>("ReceiveMessage", HandleMessageReceived);
    }

    /// <summary>
    /// Starts the connection to the SignalR hub
    /// </summary>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task StartAsync()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(SignalRClientPublisher));
        }

        if (_hubConnection.State == HubConnectionState.Disconnected)
        {
            await _hubConnection.StartAsync();
        }
    }

    /// <summary>
    /// Publishes a transport message to all subscribers
    /// </summary>
    /// <param name="message">The message to publish</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task PublishMessage(TransportMessage message)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            // Ensure the connection is established
            if (_hubConnection.State == HubConnectionState.Disconnected)
            {
                await StartAsync();
            }

            // Send the message to the hub
            await _hubConnection.SendAsync("SendMessage", message);

            // Also notify local subscribers
            NotifySubscribers(message);
        }
        catch (Exception ex)
        {
            // Log the exception in a real application
            Console.WriteLine($"Error publishing message: {ex}");
        }
    }

    /// <summary>
    /// Subscribes to receive transport messages
    /// </summary>
    /// <param name="onMessageReceived">Action to call when a message is received</param>
    public void Subscribe(Action<TransportMessage> onMessageReceived)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(SignalRClientPublisher));
        }

        lock (_subscribers)
        {
            _subscribers.Add(onMessageReceived);
        }
    }

    private void HandleMessageReceived(TransportMessage message)
    {
        if (_isDisposed)
        {
            return;
        }

        NotifySubscribers(message);
    }

    private void NotifySubscribers(TransportMessage message)
    {
        // Get a snapshot of the current subscribers
        Action<TransportMessage>[] currentSubscribers;
        lock (_subscribers)
        {
            currentSubscribers = _subscribers.ToArray();
        }

        // Notify each subscriber
        foreach (var subscriber in currentSubscribers)
        {
            try
            {
                subscriber(message);
            }
            catch (Exception ex)
            {
                // Log the exception in a real application
                Console.WriteLine($"Error notifying subscriber: {ex}");
            }
        }
    }

    /// <summary>
    /// Disposes resources used by the publisher
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        // Stop the connection if it's active
        if (_hubConnection.State != HubConnectionState.Disconnected)
        {
            await _hubConnection.StopAsync();
        }

        // Dispose the connection
        await _hubConnection.DisposeAsync();

        GC.SuppressFinalize(this);
    }
}
