using Sanet.Transport.SignalR.Discovery;
using Sanet.Transport.SignalR.Infrastructure;
using Sanet.Transport.SignalR.Publishers;

namespace Sanet.Transport.SignalR;

/// <summary>
/// Factory for creating SignalR transport publishers
/// </summary>
public static class SignalRTransportFactory
{
    /// <summary>
    /// Creates a host that can accept client connections
    /// </summary>
    /// <param name="port">Port to host on (default: 5000)</param>
    /// <param name="enableDiscovery">Whether to enable network discovery</param>
    /// <param name="discoveryPort">Port to use for discovery broadcasts (default: 5001)</param>
    /// <returns>A host manager containing the publisher</returns>
    public static async Task<SignalRHostManager> CreateHost(
        int port = 5000, 
        bool enableDiscovery = true,
        int discoveryPort = 5001)
    {
        var hostManager = new SignalRHostManager(port);
        await hostManager.Start();
        
        if (enableDiscovery)
        {
            var discovery = new MulticastDiscoveryService(discoveryPort);
            discovery.BroadcastPresence(hostManager.HubUrl);
        }
        
        return hostManager;
    }
    
    /// <summary>
    /// Creates a client publisher that connects to a host
    /// </summary>
    /// <param name="hubUrl">URL of the SignalR hub to connect to</param>
    /// <returns>A client publisher</returns>
    public static SignalRClientPublisher CreateClient(string hubUrl)
    {
        return new SignalRClientPublisher(hubUrl);
    }
    
    /// <summary>
    /// Discovers hosts on the local network
    /// </summary>
    /// <param name="timeoutSeconds">How long to search for hosts</param>
    /// <param name="discoveryPort">Port to use for discovery (default: 5001)</param>
    /// <returns>List of discovered hub URLs</returns>
    public static async Task<List<string>> DiscoverHosts(
        int timeoutSeconds = 5,
        int discoveryPort = 5001)
    {
        var discoveredHosts = new List<string>();
        using var discovery = new MulticastDiscoveryService(discoveryPort);
        
        discovery.HostDiscovered += url => 
        {
            if (!discoveredHosts.Contains(url))
                discoveredHosts.Add(url);
        };
        
        discovery.StartListening();
        await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
        discovery.StopListening();
        
        return discoveredHosts;
    }
}
