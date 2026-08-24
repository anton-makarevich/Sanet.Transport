namespace Sanet.Transport.SignalR.Client.Publishers;

/// <summary>
/// Categorizes why publishing a transport message failed.
/// </summary>
public enum PublishFailureReason
{
    /// <summary>The bounded outbound queue is full; the message was rejected.</summary>
    QueueFull,

    /// <summary>The publisher is not connected and no rebuild is in progress.</summary>
    NotConnected
}

/// <summary>
/// Thrown by <see cref="RelayClientPublisher.PublishMessage"/> when a message cannot be
/// accepted. Callers may catch this single exception type and retry based on
/// <see cref="Reason"/>.
/// </summary>
public sealed class TransportPublishException : Exception
{
    /// <summary>
    /// Gets the reason the publish attempt was rejected.
    /// </summary>
    public PublishFailureReason Reason { get; }

    /// <summary>
    /// Creates a new instance of <see cref="TransportPublishException"/>.
    /// </summary>
    /// <param name="reason">The failure reason.</param>
    /// <param name="message">The error message.</param>
    public TransportPublishException(PublishFailureReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>
    /// Creates a new instance of <see cref="TransportPublishException"/>.
    /// </summary>
    /// <param name="reason">The failure reason.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this one, if any.</param>
    public TransportPublishException(PublishFailureReason reason, string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }
}
