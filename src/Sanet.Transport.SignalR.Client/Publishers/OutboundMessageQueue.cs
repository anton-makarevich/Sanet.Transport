using Microsoft.Extensions.Logging;

namespace Sanet.Transport.SignalR.Client.Publishers;

/// <summary>
/// Bounded FIFO queue holding transport messages published while the relay connection
/// is reconnecting or being rebuilt. Messages are flushed in order once connectivity
/// is restored; when the queue is full, <see cref="EnqueueOrThrow"/> throws a
/// <see cref="TransportPublishException"/> with <see cref="PublishFailureReason.QueueFull"/>.
/// </summary>
internal sealed class OutboundMessageQueue
{
    private readonly Lock _lock = new();
    private readonly Queue<TransportMessage> _messages = new();

    public OutboundMessageQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Outbound queue capacity must be greater than zero.");
        }

        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _messages.Count;
            }
        }
    }

    /// <summary>
    /// Appends a message to the queue, throwing <see cref="TransportPublishException"/>
    /// with <see cref="PublishFailureReason.QueueFull"/> once the capacity is reached.
    /// </summary>
    public void EnqueueOrThrow(TransportMessage message, ILogger logger)
    {
        lock (_lock)
        {
            if (_messages.Count >= Capacity)
            {
                logger.LogWarning(
                    "Message rejected: outbound queue is full ({Capacity} messages)",
                    Capacity);
                throw new TransportPublishException(
                    PublishFailureReason.QueueFull,
                    $"Outbound queue is full ({Capacity} messages).");
            }

            _messages.Enqueue(message);
        }
    }

    /// <summary>
    /// Dequeues the oldest message, or returns null when the queue is empty.
    /// </summary>
    public TransportMessage? TryDequeue()
    {
        lock (_lock)
        {
            return _messages.Count > 0 ? _messages.Dequeue() : null;
        }
    }

    /// <summary>
    /// Puts a failed message back at the head of the queue, ahead of any messages
    /// enqueued mid-flush, so FIFO delivery order is preserved for the next attempt.
    /// </summary>
    public void RequeueAhead(TransportMessage message)
    {
        lock (_lock)
        {
            var remaining = _messages.ToArray();
            _messages.Clear();
            _messages.Enqueue(message);
            foreach (var queued in remaining)
            {
                _messages.Enqueue(queued);
            }
        }
    }
}
