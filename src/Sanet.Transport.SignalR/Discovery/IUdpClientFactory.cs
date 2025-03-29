namespace Sanet.Transport.SignalR.Discovery;

/// <summary>
/// Factory interface for creating IUdpClientWrapper instances.
/// </summary>
public interface IUdpClientFactory
{
    /// <summary>
    /// Creates a UDP client wrapper configured for sending.
    /// </summary>
    IUdpClientWrapper CreateSenderClient();

    /// <summary>
    /// Creates a UDP client wrapper configured for listening on a specific port.
    /// </summary>
    /// <param name="port">The port to listen on.</param>
    IUdpClientWrapper CreateListenerClient(int port);
}
