namespace Sanet.Transport.SignalR.Discovery
{
    /// <summary>
    /// Defines the contract for a service that discovers SignalR hosts on the network.
    /// </summary>
    public interface IDiscoveryService : IDisposable
    {
        /// <summary>
        /// Event raised when a SignalR host is discovered on the network.
        /// The string argument is the URL of the discovered host.
        /// </summary>
        event Action<string>? HostDiscovered;

        /// <summary>
        /// Starts listening for SignalR host announcements on the network.
        /// </summary>
        void StartListening();

        /// <summary>
        /// Stops listening for SignalR host announcements.
        /// </summary>
        void StopListening();
        
        /// <summary>
        /// Starts broadcasting the presence of a SignalR host with the specified hub URL.
        /// </summary>
        /// <param name="hubUrl">The URL of the SignalR hub to broadcast.</param>
        void BroadcastPresence(string hubUrl);

        /// <summary>
        /// Stops broadcasting the presence of the SignalR host.
        /// </summary>
        void StopBroadcasting();
    }
}
