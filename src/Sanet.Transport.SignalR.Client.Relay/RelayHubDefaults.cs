namespace Sanet.Transport.SignalR.Client.Relay;

/// <summary>
/// Well-known paths of the relay hub used by relay transports.
/// </summary>
public static class RelayHubDefaults
{
    /// <summary>
    /// Route of the SignalR <c>RelayHub</c> (see <c>RelayAuthenticationDefaults</c> in the Hub server).
    /// </summary>
    public const string HubPath = "/hubs/relay";

    /// <summary>
    /// Builds the relay hub URL from a hub base URL by appending <see cref="HubPath"/>.
    /// </summary>
    /// <param name="baseUrl">Base URL of the relay hub (may include a trailing slash).</param>
    public static string BuildHubUrl(string baseUrl)
    {
        return $"{baseUrl.Trim().TrimEnd('/')}{HubPath}";
    }
}
