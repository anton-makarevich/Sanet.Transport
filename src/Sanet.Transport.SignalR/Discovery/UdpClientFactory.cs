namespace Sanet.Transport.SignalR.Discovery;

/// <summary>
/// Concrete factory for creating UdpClientWrapper instances.
/// </summary>
public class UdpClientFactory : IUdpClientFactory
{
    public IUdpClientWrapper CreateSenderClient() => new UdpClientWrapper();

    public IUdpClientWrapper CreateListenerClient(int port) => new UdpClientWrapper(port);
}
