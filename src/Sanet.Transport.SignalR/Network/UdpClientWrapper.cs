using System.Net;
using System.Net.Sockets;

namespace Sanet.Transport.SignalR.Network;

/// <summary>
/// Concrete implementation of IUdpClientWrapper using System.Net.Sockets.UdpClient.
/// </summary>
public class UdpClientWrapper : IUdpClientWrapper
{
    private readonly UdpClient _udpClient;

    // Constructor for listener client
    public UdpClientWrapper(int port)
    {
        _udpClient = new UdpClient(port);
    }

    // Constructor for sender client
    public UdpClientWrapper()
    {
        _udpClient = new UdpClient();
    }

    public Task<int> SendAsync(byte[] datagram, int bytes, IPEndPoint endPoint) =>
        _udpClient.SendAsync(datagram, bytes, endPoint);

    public Task<UdpReceiveResult> ReceiveAsync() => _udpClient.ReceiveAsync();

    public void JoinMulticastGroup(IPAddress multicastAddr) => _udpClient.JoinMulticastGroup(multicastAddr);

    public void DropMulticastGroup(IPAddress multicastAddr) => _udpClient.DropMulticastGroup(multicastAddr);

    public void Close() => _udpClient.Close();

    public void Dispose()
    {
        _udpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
