using Sanet.Transport.SignalR.Discovery;

namespace Sanet.Transport.SignalR.Network;

/// <summary>
/// Concrete factory for creating UdpClientWrapper instances.
/// </summary>
public class UdpClientFactory : IUdpClientFactory
{
    public IUdpClientWrapper CreateSenderClient() => new UdpClientWrapper();

    public IUdpClientWrapper CreateListenerClient(int port) => new UdpClientWrapper(port);
}
