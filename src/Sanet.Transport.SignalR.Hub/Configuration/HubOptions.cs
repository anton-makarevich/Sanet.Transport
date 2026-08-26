namespace Sanet.Transport.SignalR.Hub.Configuration;

/// <summary>
/// Configuration for the relay hub's infrastructure limits and shared API key.
/// </summary>
public sealed class HubOptions
{
    public const string SectionName = "Hub";

    /// <summary>
    /// Shared key required by REST callers. It is intentionally supplied by deployment configuration.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// The maximum number of non-expired rooms the relay accepts at one time.
    /// </summary>
    public int MaxConcurrentRooms { get; init; } = 100;

    /// <summary>
    /// Maximum number of join attempts per minute per IP address.
    /// </summary>
    public int JoinRateLimitPerMinute { get; init; } = 10;

    /// <summary>
    /// Maximum number of <c>Relay()</c> calls per minute per SignalR connection.
    /// </summary>
    public int RelayRateLimitPerMinute { get; init; } = 120;

    /// <summary>
    /// Maximum length of <see cref="Relay.RelayEnvelope.Payload"/> accepted by <c>Relay()</c>.
    /// </summary>
    public int MaxRelayPayloadBytes { get; init; } = 256 * 1024;

    /// <summary>
    /// Time-to-live in seconds for rooms. A room is garbage-collected after
    /// this duration of inactivity. Applies to all room states.
    /// </summary>
    public int RoomTtlSeconds { get; init; } = 7200;

    /// <summary>
    /// Grace period in seconds after the host disconnects before the room
    /// is permanently dissolved. Allows brief transport blips without
    /// destroying the session.
    /// </summary>
    public int DissolutionGracePeriodSeconds { get; init; } = 30;

    /// <summary>
    /// Time-to-live in seconds for relay tickets minted via the REST relay-ticket endpoint.
    /// A ticket is only needed at connection/negotiation time; once authenticated, a
    /// connection stays authenticated for the room session. Reconnects within this window
    /// re-present the same ticket and are accepted.
    /// </summary>
    public int RelayTicketTtlSeconds { get; init; } = 60;

    /// <summary>
    /// How long in seconds a connecting client waits for the room host's SignalR
    /// connection to register before its <c>OnPeerConnected</c> announcement is
    /// dropped. Covers the race where a fast client completes its handshake while
    /// the host is still finishing its own. Zero disables waiting.
    /// </summary>
    public int HostConnectionWaitSeconds { get; init; } = 5;

    /// <summary>
    /// Delay in seconds before the host is notified that a peer disconnected.
    /// A reconnect of the same device session within the delay cancels the
    /// notification. Zero reproduces immediate-notification behavior.
    /// </summary>
    public int PeerDisconnectNotificationDelaySeconds { get; init; } = 5;

    /// <summary>
    /// SignalR transport tuning that controls how fast an ungraceful disconnect
    /// is detected by the server.
    /// </summary>
    public SignalROptions SignalR { get; init; } = new();

    /// <summary>
    /// Trusted proxy CIDRs for ForwardedHeaders (comma-separated).
    /// </summary>
    public string[] TrustedProxies { get; init; } = [];
}

/// <summary>
/// SignalR keep-alive and client-timeout settings for the relay hub transport.
/// </summary>
public sealed class SignalROptions
{
    public const int DefaultKeepAliveIntervalSeconds = 15;
    public const int DefaultClientTimeoutIntervalSeconds = 30;

    /// <summary>
    /// Interval between server keep-alive pings sent to clients. A shorter interval
    /// makes dead connections detectable sooner at the cost of more traffic.
    /// </summary>
    public int KeepAliveIntervalSeconds { get; init; } = DefaultKeepAliveIntervalSeconds;

    /// <summary>
    /// Maximum time a client may stay silent before the server considers the connection
    /// dead. Per SignalR guidance this should be at least twice the keep-alive interval.
    /// </summary>
    public int ClientTimeoutIntervalSeconds { get; init; } = DefaultClientTimeoutIntervalSeconds;
}
