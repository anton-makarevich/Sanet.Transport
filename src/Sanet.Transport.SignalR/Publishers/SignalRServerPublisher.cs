using Microsoft.AspNetCore.SignalR;
using Sanet.Transport.SignalR.Infrastructure;

namespace Sanet.Transport.SignalR.Publishers;

/// <summary>
/// Server-side implementation of ITransportPublisher using SignalR
/// </summary>
public class SignalRServerPublisher : ITransportPublisher, IDisposable
{
    private readonly IHubContext<TransportHub> _hubContext;
    private readonly List<Action<TransportMessage>> _subscribers = new List<Action<TransportMessage>>();
    private bool _isDisposed;

    /// <summary>
    /// Creates a new instance of SignalRServerPublisher
    /// </summary>
    /// <param name="hubContext">The SignalR hub context</param>
    public SignalRServerPublisher(IHubContext<TransportHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        
        // Subscribe to the hub's message event
        TransportHub.MessageReceived += HandleMessageReceived;
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
            // Send the message to all connected clients
            await _hubContext.Clients.All.SendAsync("ReceiveMessage", message);
            
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
            throw new ObjectDisposedException(nameof(SignalRServerPublisher));
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
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }
        
        _isDisposed = true;
        
        // Unsubscribe from the hub's message event
        TransportHub.MessageReceived -= HandleMessageReceived;
        
        GC.SuppressFinalize(this);
    }
}
