using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Sanet.Transport.Rx;

/// <summary>
/// Implementation of ITransportPublisher using Reactive Extensions
/// </summary>
public class RxTransportPublisher : ITransportPublisher
{
    private readonly Subject<TransportMessage> _messageSubject = new();
    private readonly IScheduler _scheduler;
    private TransportConnectionState _connectionState = TransportConnectionState.Connected;
    private bool _isDisposed;

    /// <summary>
    /// Gets the current transport connection state. The Rx publisher reports
    /// <see cref="TransportConnectionState.Connected"/> from construction until it is disposed.
    /// </summary>
    public TransportConnectionState ConnectionState => _connectionState;

    /// <summary>
    /// Event raised on every transport connection-state transition. Raised once with
    /// <see cref="TransportConnectionState.Closed"/> when the publisher is disposed.
    /// </summary>
    public event Action<TransportConnectionState>? ConnectionStateChanged;

    public RxTransportPublisher(IScheduler? scheduler = null)
    {
        _scheduler = scheduler ?? TaskPoolScheduler.Default;
    }

    /// <summary>
    /// Publishes a transport message to all subscribers
    /// </summary>
    /// <param name="message">The message to publish</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public Task PublishMessage(TransportMessage message)
    {
        if (_isDisposed)
        {
            return Task.CompletedTask;
        }

        _messageSubject.OnNext(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Subscribes to receive transport messages
    /// </summary>
    /// <param name="onMessageReceived">Action to call when a message is received</param>
    public void Subscribe(Action<TransportMessage> onMessageReceived)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(RxTransportPublisher));
        }

        _messageSubject
            .AsObservable()
            .ObserveOn(_scheduler)
            .Subscribe(onMessageReceived);
    }

    /// <summary>
    /// Asynchronously disposes resources used by the publisher
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return ValueTask.CompletedTask;
        }

        _isDisposed = true;
        _messageSubject.Dispose();

        _connectionState = TransportConnectionState.Closed;
        ConnectionStateChanged?.Invoke(TransportConnectionState.Closed);

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
