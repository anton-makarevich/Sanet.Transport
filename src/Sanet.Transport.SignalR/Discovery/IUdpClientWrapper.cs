using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Sanet.Transport.SignalR.Discovery;

/// <summary>
/// Interface abstracting UdpClient operations for testability.
/// </summary>
public interface IUdpClientWrapper : IDisposable
{
    Task<int> SendAsync(byte[] datagram, int bytes, IPEndPoint endPoint);
    Task<UdpReceiveResult> ReceiveAsync();
    void JoinMulticastGroup(IPAddress multicastAddr);
    void DropMulticastGroup(IPAddress multicastAddr);
    void Close();
}
