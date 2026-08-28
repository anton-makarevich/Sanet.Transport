namespace Sanet.Transport;

/// <summary>
/// Describes the connectivity state of a transport connection.
/// </summary>
public enum TransportConnectionState
{
    /// <summary>
    /// The connection is being established.
    /// </summary>
    Connecting,

    /// <summary>
    /// The connection is established and operational.
    /// </summary>
    Connected,

    /// <summary>
    /// The connection was lost and is being re-established.
    /// <para>Non-terminal: the connection may return to <see cref="Connected"/>.</para>
    /// </summary>
    Reconnecting,

    /// <summary>
    /// The connection is not active.
    /// <para>Non-terminal: a new connection attempt may transition to <see cref="Connecting"/>.</para>
    /// </summary>
    Disconnected,

    /// <summary>
    /// The connection has been closed.
    /// <para>Terminal: a new publisher must be created to establish a new connection.</para>
    /// </summary>
    Closed
}