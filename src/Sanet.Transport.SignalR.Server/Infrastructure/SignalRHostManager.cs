using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR;
using Sanet.Transport.SignalR.Server.Publishers;

namespace Sanet.Transport.SignalR.Server.Infrastructure;

/// <summary>
/// Manages a self-contained SignalR host that can be embedded in any application
/// </summary>
public class SignalRHostManager : IAsyncDisposable
{
    private IHost? _host;
    private readonly string _url;
    private readonly string _hub;
    private SignalRServerPublisher? _publisher;
    private bool _isDisposed;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

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
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(SignalRHostManager));

            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSignalR();
            builder.WebHost.UseUrls(_url);

            var app = builder.Build();

            app.UseRouting();
            app.MapHub<TransportHub>($"/{_hub}");

            _publisher = new SignalRServerPublisher(app.Services.GetRequiredService<IHubContext<TransportHub>>());

            _host = app;
            await _host.StartAsync();
        }
        finally
        {
            _lifecycleLock.Release();
        }
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
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    var ipProps = ni.GetIPProperties();

                    // Skip if no default gateway
                    var hasGateway = ipProps.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (!hasGateway)
                        continue;

                    foreach (var ip in ipProps.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            hostAddress = ip.Address.ToString();
                            break;
                        }
                    }

                    if (hostAddress != null)
                        break;
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
    /// Asynchronously disposes the host manager and stops the SignalR host
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
        }
        finally
        {
            _lifecycleLock.Release();
        }

        if (_host != null)
        {
            try
            {
                await _host.StopAsync();
            }
            finally
            {
                if (_host is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else
                    _host.Dispose();
            }
        }

        (_publisher as IDisposable)?.Dispose();

        GC.SuppressFinalize(this);
    }
}
