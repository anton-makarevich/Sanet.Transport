using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Sanet.Transport.SignalR.Discovery;

/// <summary>
/// Provides network discovery for SignalR hosts on the local network using UDP Multicast
/// </summary>
public class SignalRDiscoveryService : IDisposable
{
    private const int DefaultDiscoveryPort = 5001;
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.0.0.1"); // Multicast address, we can make it configurable
    private readonly int _discoveryPort;
    private UdpClient? _listenerClient;
    private bool _isBroadcasting;
    private bool _isListening;
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
    /// Broadcasts this host's presence on the network via Multicast
    /// </summary>
    /// <param name="hubUrl">The URL of the SignalR hub</param>
    public void BroadcastPresence(string hubUrl)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SignalRDiscoveryService));

        if (_isBroadcasting) return; // Prevent multiple broadcast loops
        _isBroadcasting = true;

        Task.Run(async () =>
        {
            // Use a separate client for sending to avoid potential conflicts with listening
            using var senderClient = new UdpClient();
            // Note: No JoinMulticastGroup needed for sending
            var endpoint = new IPEndPoint(MulticastAddress, _discoveryPort);
            var data = Encoding.UTF8.GetBytes(hubUrl);

            while (_isBroadcasting && !_isDisposed)
            {
                try
                {
                    await senderClient.SendAsync(data, data.Length, endpoint);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error broadcasting presence via multicast: {ex.Message}");
                    // Consider adding a delay or stopping broadcast on persistent errors
                }

                await Task.Delay(5000); // Broadcast every 5 seconds
            }
            _isBroadcasting = false; // Mark as stopped
            Console.WriteLine("Stopped broadcasting presence."); // Optional: Add logging
        });
    }

    /// <summary>
    /// Starts listening for host broadcasts via Multicast
    /// </summary>
    public void StartListening()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SignalRDiscoveryService));

        if (_isListening) return; // Prevent multiple listener loops

        try
        {
            _listenerClient = new UdpClient(_discoveryPort);
            _listenerClient.JoinMulticastGroup(MulticastAddress);
            Console.WriteLine($"Joined multicast group {MulticastAddress} on port {_discoveryPort}"); // Optional logging
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize multicast listener: {ex.Message}");
            _listenerClient?.Dispose();
            _listenerClient = null;
            return; // Don't start the listening task if setup failed
        }

        _isListening = true;

        Task.Run(async () =>
        {
            while (_isListening && !_isDisposed && _listenerClient != null)
            {
                try
                {
                    var result = await _listenerClient.ReceiveAsync();
                    var hubUrl = Encoding.UTF8.GetString(result.Buffer);
                    HostDiscovered?.Invoke(hubUrl);
                }
                catch (ObjectDisposedException) 
                {
                    // Expected when closing the client, break the loop
                    break; 
                }
                catch (Exception ex)
                {
                    if (!_isDisposed) // Only log if not disposed intentionally
                    {
                        Console.WriteLine($"Error receiving discovery multicast: {ex.Message}");
                        // Retry mechanism/circuit breaker can be added
                    }
                    else
                    {
                        break; // Exit loop if disposed
                    }
                }
            }
            _isListening = false; // Mark as stopped
            Console.WriteLine("Stopped listening for discovery broadcasts."); // Optional: Add logging
        });
    }

    /// <summary>
    /// Stops broadcasting and listening
    /// </summary>
    public void Stop()
    {
        _isBroadcasting = false;
        _isListening = false; // Signal listening loop to stop
        
        // Closing the listener client will cause ReceiveAsync to throw ObjectDisposedException,
        // which is handled in the listening loop to break out.
        CloseAndDisposeListener();
    }

    /// <summary>
    /// Disposes the discovery service
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        Stop(); // Ensure broadcasting and listening are stopped

        GC.SuppressFinalize(this);
    }

    private void CloseAndDisposeListener()
    {
        if (_listenerClient == null) return;
        try
        {
            // Check if joined before trying to drop
            // This might require tracking the joined state explicitly if needed,
            // but often just trying to drop is sufficient and handles cases where join failed.
            _listenerClient.DropMulticastGroup(MulticastAddress);
            Console.WriteLine($"Left multicast group {MulticastAddress}"); // Optional logging
        }
        catch (Exception ex) 
        {
            // Log error if dropping fails, but continue disposal
            Console.WriteLine($"Error dropping multicast group: {ex.Message}");
        }
        _listenerClient.Close();
        _listenerClient.Dispose();
        _listenerClient = null;
    }
}
