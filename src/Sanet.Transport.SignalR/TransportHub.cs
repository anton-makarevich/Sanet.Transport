using Microsoft.AspNetCore.SignalR;

namespace Sanet.Transport.SignalR;

/// <summary>
/// SignalR Hub for transport message communication
/// </summary>
public class TransportHub : Hub
{
    /// <summary>
    /// Event that fires when a message is received from a client
    /// </summary>
    internal static event Action<TransportMessage>? OnMessageReceived;
    
    /// <summary>
    /// Receives a message from a client and broadcasts it to all connected clients
    /// </summary>
    /// <param name="message">The transport message to broadcast</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task SendMessage(TransportMessage message)
    {
        // Invoke the event to notify local subscribers
        OnMessageReceived?.Invoke(message);
        
        // Broadcast the message to all connected clients
        await Clients.All.SendAsync("ReceiveMessage", message);
    }
    
    /// <summary>
    /// For testing purposes only - allows tests to simulate a message received event
    /// </summary>
    internal static void SimulateMessageReceived(TransportMessage message)
    {
        OnMessageReceived?.Invoke(message);
    }
}
