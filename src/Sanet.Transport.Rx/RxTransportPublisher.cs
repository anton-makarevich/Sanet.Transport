using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Sanet.Transport.Rx;

/// <summary>
/// Implementation of ITransportPublisher using Reactive Extensions
/// </summary>
public class RxTransportPublisher : ITransportPublisher
{
    private readonly Subject<TransportMessage> _messageSubject = new();

    /// <summary>
    /// Publishes a transport message to all subscribers
    /// </summary>
    /// <param name="message">The message to publish</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public Task PublishMessage(TransportMessage message)
    {
        _messageSubject.OnNext(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Subscribes to receive transport messages
    /// </summary>
    /// <param name="onMessageReceived">Action to call when a message is received</param>
    public void Subscribe(Action<TransportMessage> onMessageReceived)
    {
        _messageSubject.AsObservable().Subscribe(onMessageReceived);
    }
}
