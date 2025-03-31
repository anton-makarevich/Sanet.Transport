using System.Net;
using System.Text;
using Sanet.Transport.SignalR.Network;

namespace Sanet.Transport.SignalR.Discovery;
    
/// <summary>
/// Provides network discovery for SignalR hosts on the local network
/// </summary>
public class BroadcastDiscoveryService : IDiscoveryService
{
    private const int DefaultDiscoveryPort = 5001;
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
    public BroadcastDiscoveryService(int discoveryPort = DefaultDiscoveryPort)
        : this(new UdpClientFactory(), discoveryPort)
    {
    }

    /// <summary>
    /// Creates a new instance of the SignalRDiscoveryService with a specific UDP client factory.
    /// Used primarily for testing.
    /// </summary>
    /// <param name="udpClientFactory">The factory to create UDP client wrappers.</param>
    /// <param name="discoveryPort">Optional port to use for discovery (default: 5001)</param>
    public BroadcastDiscoveryService(IUdpClientFactory udpClientFactory, int discoveryPort = DefaultDiscoveryPort)
    {
        _udpClientFactory = udpClientFactory ?? throw new ArgumentNullException(nameof(udpClientFactory));
        _discoveryPort = discoveryPort;
    }

    public void StopListening()
    {
        _isListening = false; // Signal listening loop to stop
        CloseAndDisposeListener();
    }

    /// <summary>
    /// Broadcasts this host's presence on the network
    /// </summary>
    /// <param name="hubUrl">The URL of the SignalR hub</param>
    public void BroadcastPresence(string hubUrl)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(BroadcastDiscoveryService));

        if (_isBroadcasting) return; // Prevent multiple broadcast loops
        _isBroadcasting = true;
        
        Task.Run(async () =>
        {
            // Use a separate client for sending
            using var senderClient = _udpClientFactory.CreateSenderClient();
            var endpoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
            var data = Encoding.UTF8.GetBytes(hubUrl);
            
            while (_isBroadcasting && !_isDisposed)
            {
                try
                {
                    await senderClient.SendAsync(data, data.Length, endpoint);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error broadcasting presence: {ex.Message}");
                }
                
                await Task.Delay(5000); // Broadcast every 5 seconds
            }
            _isBroadcasting = false; // Mark as stopped
            Console.WriteLine("Stopped broadcasting presence."); // Optional: Add logging
        });
    }

    public void StopBroadcasting()
    {
        _isBroadcasting = false;
    }

    /// <summary>
    /// Starts listening for host broadcasts
    /// </summary>
    public void StartListening()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(BroadcastDiscoveryService));

        if (_isListening) return; // Prevent multiple listener loops

        try
        {
            // Dispose previous listener if any (shouldn't happen with _isListening check, but defensive)
            CloseAndDisposeListener();

            _listenerClient = _udpClientFactory.CreateListenerClient(_discoveryPort);
            Console.WriteLine($"Started listening on port {_discoveryPort}"); // Optional logging
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize listener: {ex.Message}");
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
                        Console.WriteLine($"Error receiving discovery broadcast: {ex.Message}");
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
        StopListening();
        StopBroadcasting();
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
            // Stop loops and dispose managed resources
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
            _listenerClient.Close();
            Console.WriteLine("Closed listener client"); // Optional logging
        }
        catch (Exception ex) 
        {
            // Log error if closing fails, but continue disposal
            Console.WriteLine($"Error closing listener client: {ex.Message}");
        }
        _listenerClient.Dispose();
        _listenerClient = null;
    }
}
