using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sanet.Transport.SignalR;

/// <summary>
/// Factory for creating SignalR transport publishers
/// </summary>
public static class SignalRTransportFactory
{
    /// <summary>
    /// Creates a host that can accept client connections
    /// </summary>
    /// <param name="url">URL to host on (default: http://0.0.0.0:5000)</param>
    /// <param name="enableDiscovery">Whether to enable network discovery</param>
    /// <returns>A host manager containing the publisher</returns>
    public static async Task<SignalRHostManager> CreateHostAsync(
        string url = "http://0.0.0.0:5000", 
        bool enableDiscovery = true)
    {
        var hostManager = new SignalRHostManager(url);
        await hostManager.StartAsync();
        
        if (enableDiscovery)
        {
            var discovery = new SignalRDiscoveryService();
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
    /// <returns>List of discovered hub URLs</returns>
    public static async Task<List<string>> DiscoverHostsAsync(int timeoutSeconds = 5)
    {
        var discoveredHosts = new List<string>();
        using var discovery = new SignalRDiscoveryService();
        
        discovery.HostDiscovered += url => 
        {
            if (!discoveredHosts.Contains(url))
                discoveredHosts.Add(url);
        };
        
        discovery.StartListening();
        await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
        discovery.Stop();
        
        return discoveredHosts;
    }
}
