namespace Sanet.Transport.SignalR.Client.Relay;

/// <summary>
/// Error codes returned by the relay hub.
/// </summary>
public enum HubErrorCode
{
    HubAtCapacity,
    RoomNotFound,
    RoomExpired,
    HostNotReady,
    NotHost,
    RateLimited,
    MessageTooLarge,
    HostPlayerIdConflict,
    RoomFull,
    InvalidApiKey,
    InvalidSessionToken,
    HostDisconnected,
    ConnectionSuperseded
}
