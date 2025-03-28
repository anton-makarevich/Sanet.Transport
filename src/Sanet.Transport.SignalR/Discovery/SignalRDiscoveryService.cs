using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Sanet.Transport.SignalR.Discovery;

/// <summary>
/// Provides network discovery for SignalR hosts on the local network
/// </summary>
public class SignalRDiscoveryService : IDisposable
{
    private const int DefaultDiscoveryPort = 5001;
    private readonly int _discoveryPort;
    private UdpClient? _client;
    private bool _isRunning;
    private bool _isDisposed;

    /// <summary>
    /// Event raised when a SignalR host is discovered
    /// </summary>
    public event Action<string>? HostDiscovered;

    /// <summary>
    /// Creates a new instance of the SignalRDiscoveryService
    /// </summary>
    /// <param name="discoveryPort">Optional port to use for discovery (default: 5001)</param>
    public SignalRDiscoveryService(int discoveryPort = DefaultDiscoveryPort)
    {
        _discoveryPort = discoveryPort;
    }

    /// <summary>
    /// Broadcasts this host's presence on the network
    /// </summary>
    /// <param name="hubUrl">The URL of the SignalR hub</param>
    public void BroadcastPresence(string hubUrl)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SignalRDiscoveryService));

        _isRunning = true;
        
        Task.Run(async () =>
        {
            using var client = new UdpClient();
            client.EnableBroadcast = true;
            var endpoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
            var data = Encoding.UTF8.GetBytes(hubUrl);
            
            while (_isRunning && !_isDisposed)
            {
                try
                {
                    await client.SendAsync(data, data.Length, endpoint);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error broadcasting presence: {ex.Message}");
                }
                
                await Task.Delay(5000); // Broadcast every 5 seconds
            }
        });
    }

    /// <summary>
    /// Starts listening for host broadcasts
    /// </summary>
    public void StartListening()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SignalRDiscoveryService));

        _isRunning = true;
        _client = new UdpClient(_discoveryPort);
        
        Task.Run(async () =>
        {
            while (_isRunning && !_isDisposed)
            {
                try
                {
                    var result = await _client.ReceiveAsync();
                    var hubUrl = Encoding.UTF8.GetString(result.Buffer);
                    HostDiscovered?.Invoke(hubUrl);
                }
                catch (Exception ex)
                {
                    if (!_isDisposed) // Only log if not disposed
                    {
                        Console.WriteLine($"Error receiving discovery broadcast: {ex.Message}");
                    }
                }
            }
        });
    }

    /// <summary>
    /// Stops broadcasting and listening
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
    }

    /// <summary>
    /// Disposes the discovery service
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _isRunning = false;
        
        if (_client != null)
        {
            _client.Close();
            _client.Dispose();
            _client = null;
        }
        
        GC.SuppressFinalize(this);
    }
}
