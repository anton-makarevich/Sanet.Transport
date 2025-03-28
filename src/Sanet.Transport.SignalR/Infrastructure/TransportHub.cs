using Microsoft.AspNetCore.SignalR;

namespace Sanet.Transport.SignalR.Infrastructure;

/// <summary>
/// SignalR hub for transport message communication
/// </summary>
public class TransportHub : Hub
{
    /// <summary>
    /// Event raised when a message is received from a client
    /// </summary>
    internal static event Action<TransportMessage>? MessageReceived;

    /// <summary>
    /// Called by clients to send a message
    /// </summary>
    /// <param name="message">The transport message to send</param>
    public async Task SendMessage(TransportMessage message)
    {
        // Notify local subscribers
        MessageReceived?.Invoke(message);
        
        // Forward to all clients except the sender
        await Clients.Others.SendAsync("ReceiveMessage", message);
    }
    
    /// <summary>
    /// Simulates a message being received (for testing)
    /// </summary>
    /// <param name="message">The message to simulate</param>
    internal static void SimulateMessageReceived(TransportMessage message)
    {
        MessageReceived?.Invoke(message);
    }
}
