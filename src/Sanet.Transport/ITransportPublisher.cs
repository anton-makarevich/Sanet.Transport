namespace Sanet.Transport;

/// <summary>
/// Interface for transport publisher implementations
/// </summary>
public interface ITransportPublisher
{
    /// <summary>
    /// Publishes a transport message
    /// </summary>
    /// <param name="message">The message to publish</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task PublishMessage(TransportMessage message);
    
    /// <summary>
    /// Subscribes to receive transport messages
    /// </summary>
    /// <param name="onMessageReceived">Action to call when a message is received</param>
    void Subscribe(Action<TransportMessage> onMessageReceived);
}
