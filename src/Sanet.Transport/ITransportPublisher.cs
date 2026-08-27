namespace Sanet.Transport;

/// <summary>
/// Interface for transport publisher implementations
/// </summary>
public interface ITransportPublisher : IAsyncDisposable
{
    /// <summary>
    /// Gets the current transport connection state.
    /// </summary>
    TransportConnectionState ConnectionState { get; }

    /// <summary>
    /// Event raised on every transport connection-state transition.
    /// <para>Reports transport connectivity only, not room membership: it is not raised when
    /// peers or hosts connect or disconnect from a room.</para>
    /// </summary>
    event Action<TransportConnectionState>? ConnectionStateChanged;

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
