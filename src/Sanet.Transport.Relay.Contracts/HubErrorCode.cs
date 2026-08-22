namespace Sanet.Transport.Relay.Contracts;

/// <summary>
/// Error codes returned by the relay's REST contract. Serialized as string names via
/// <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>; values are pinned
/// so both Hub and clients stay in sync even if names are reordered.
/// </summary>
public enum HubErrorCode
{
    HubAtCapacity = 0,
    RoomNotFound = 1,
    RoomExpired = 2,
    HostNotReady = 3,
    NotHost = 4,
    RateLimited = 5,
    MessageTooLarge = 6,
    RoomFull = 7,
    InvalidRoomState = 8,
    MemberNotFound = 9,
    CannotRemoveHost = 10,
    HostDisconnected = 11,
    ConnectionSuperseded = 12
}
