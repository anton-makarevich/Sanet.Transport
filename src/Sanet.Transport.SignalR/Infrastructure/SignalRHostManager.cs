using Microsoft.AspNetCore.SignalR;
using Sanet.Transport.SignalR.Publishers;

namespace Sanet.Transport.SignalR.Infrastructure;

/// <summary>
/// Manages a self-contained SignalR host that can be embedded in any application
/// </summary>
public class SignalRHostManager : IDisposable
{
    private IHost? _host;
    private readonly string _url;
    private readonly string _hub;
    private SignalRServerPublisher? _publisher;
    private bool _isDisposed;

    /// <summary>
    /// Creates a new SignalR host manager
    /// </summary>
    /// <param name="port">Port to host the SignalR hub on (e.g., "http://0.0.0.0:5000")</param>
    /// <param name="hub">Hub name</param>
    public SignalRHostManager(int port = 5000, string hub = "transporthub")
    {
        _url =$"http://0.0.0.0:{port}";
        _hub = hub;
    }

    /// <summary>
    /// Starts the SignalR host
    /// </summary>
    public async Task Start()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SignalRHostManager));

        var builder = WebApplication.CreateBuilder();
        
        // Add SignalR services
        builder.Services.AddSignalR();
        builder.WebHost.UseUrls(_url);
        
        // Build the app
        var app = builder.Build();
        
        // Configure the HTTP request pipeline
        app.UseRouting();
        app.MapHub<TransportHub>($"/{_hub}");
        
        // Create the publisher
        _publisher = new SignalRServerPublisher(app.Services.GetRequiredService<IHubContext<TransportHub>>());
        
        // Start the host
        _host = app;
        await _host.StartAsync();
    }

    /// <summary>
    /// Gets the transport publisher associated with this host
    /// </summary>
    public ITransportPublisher Publisher 
    { 
        get
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(SignalRHostManager));
                
            if (_publisher == null)
                throw new InvalidOperationException("Host has not been started. Call StartAsync first.");
                
            return _publisher;
        }
    }

    /// <summary>
    /// Gets the URL where the SignalR hub is hosted
    /// </summary>
    public string HubUrl
    {
        get
        {
            // Replace 0.0.0.0 with a routable address
            // First try to get the machine's LAN IP address
            var hostAddress = "localhost"; // Default fallback
            
            try
            {
                // Get the machine's IP address that's not a loopback address
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    // Prefer IPv4 addresses on the LAN
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        hostAddress = ip.ToString();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting IP address: {ex}");
                // If we can't get the IP, fall back to localhost
            }
            
            // Replace the non-routable address with the actual IP
            var url = _url.Replace("0.0.0.0", hostAddress);
            return $"{url}/{_hub}";
        }
    }

    /// <summary>
    /// Disposes the host manager and stops the SignalR host
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        
        if (_host != null)
        {
            _host.StopAsync().Wait();
            _host.Dispose();
        }
        
        (_publisher as IDisposable)?.Dispose();
        
        GC.SuppressFinalize(this);
    }
}
