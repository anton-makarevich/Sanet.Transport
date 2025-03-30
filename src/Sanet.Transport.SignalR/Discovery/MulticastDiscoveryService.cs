using System.Net;
using System.Text;

namespace Sanet.Transport.SignalR.Discovery;

/// <summary>
/// Provides network discovery for SignalR hosts on the local network using UDP Multicast
/// </summary>
public class MulticastDiscoveryService : IDiscoveryService
{
    private const int DefaultDiscoveryPort = 5001;
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.0.0.1"); // Multicast address, we can make it configurable
    private readonly int _discoveryPort;
    private readonly IUdpClientFactory _udpClientFactory;
    private IUdpClientWrapper? _listenerClient;
    private bool _isBroadcasting;
    private bool _isListening;
    private bool _isDisposed;

    /// <summary>
    /// Event raised when a SignalR host is discovered
    /// </summary>
    public event Action<string>? HostDiscovered;

    /// <summary>
    /// Creates a new instance of the SignalRDiscoveryService using the default UdpClientFactory.
    /// </summary>
    /// <param name="discoveryPort">Optional port to use for discovery (default: 5001)</param>
    public MulticastDiscoveryService(int discoveryPort = DefaultDiscoveryPort)
        : this(new UdpClientFactory(), discoveryPort)
    {
    }

    /// <summary>
    /// Creates a new instance of the SignalRDiscoveryService with a specific UDP client factory.
    /// Used primarily for testing.
    /// </summary>
    /// <param name="udpClientFactory">The factory to create UDP client wrappers.</param>
    /// <param name="discoveryPort">Optional port to use for discovery (default: 5001)</param>
    public MulticastDiscoveryService(IUdpClientFactory udpClientFactory, int discoveryPort = DefaultDiscoveryPort)
    {
        _udpClientFactory = udpClientFactory ?? throw new ArgumentNullException(nameof(udpClientFactory));
        _discoveryPort = discoveryPort;
    }

    /// <summary>
    /// Broadcasts this host's presence on the network via Multicast
    /// </summary>
    /// <param name="hubUrl">The URL of the SignalR hub</param>
    public void BroadcastPresence(string hubUrl)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(MulticastDiscoveryService));

        if (_isBroadcasting) return; // Prevent multiple broadcast loops
        _isBroadcasting = true;

        Task.Run(async () =>
        {
            // Use a separate client for sending
            using var senderClient = _udpClientFactory.CreateSenderClient();
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
            throw new ObjectDisposedException(nameof(MulticastDiscoveryService));

        if (_isListening) return; // Prevent multiple listener loops

        try
        {
            // Dispose previous listener if any (shouldn't happen with _isListening check, but defensive)
            CloseAndDisposeListener(); 

            _listenerClient = _udpClientFactory.CreateListenerClient(_discoveryPort);
            _listenerClient.JoinMulticastGroup(MulticastAddress);
            Console.WriteLine($"Joined multicast group {MulticastAddress} on port {_discoveryPort}"); // Optional logging
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize multicast listener: {ex.Message}");
            _listenerClient?.Dispose(); // Attempt to dispose if creation failed partially
            _listenerClient = null;
            return; // Don't start the listening task if setup failed
        }

        _isListening = true;

        Task.Run(async () =>
        {
            // Capture the client instance for the task closure
            var currentListenerClient = _listenerClient;
            if (currentListenerClient == null) return; // Should not happen if setup succeeded

            while (_isListening && !_isDisposed)
            {
                try
                {
                    var result = await currentListenerClient.ReceiveAsync();
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
    /// Stops listening for host broadcasts
    /// </summary>
    public void StopListening()
    {
        _isListening = false; // Signal listening loop to stop
        CloseAndDisposeListener();
    }

    /// <summary>
    /// Stops broadcasting this host's presence
    /// </summary>
    public void StopBroadcasting()
    {
        _isBroadcasting = false;
    }

    /// <summary>
    /// Disposes the discovery service
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_isDisposed)
            return;

        if (disposing)
        {
            // Stop loops and dispose managed resources (UDP client)
            StopListening();
            StopBroadcasting();
        }

        // Dispose unmanaged resources here if any

        _isDisposed = true;
    }

    private void CloseAndDisposeListener()
    {
        if (_listenerClient == null) return;
        try
        {
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
