namespace Sanet.Transport.SignalR.Discovery;

public static class DiscoveryServiceExtensions
{
    /// <summary>
    /// Discovers hosts on the local network
    /// </summary>
    /// <param name="discoveryService">DiscoveryService to extend</param>
    /// <param name="timeoutSeconds">How long to search for hosts</param>
    /// <returns>List of discovered hub URLs</returns>
    public static async Task<List<string>> DiscoverHosts( this IDiscoveryService discoveryService,
        int timeoutSeconds = 5)
    {
        var discoveredHosts = new List<string>();
        
        discoveryService.HostDiscovered += url => 
        {
            if (!discoveredHosts.Contains(url))
                discoveredHosts.Add(url);
        };
        
        discoveryService.StartListening();
        await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
        discoveryService.StopListening();
        
        return discoveredHosts;
    }
}