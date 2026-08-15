namespace Sanet.Transport.SignalR.Client.Relay.Contracts;

/// <summary>
/// Wire enum mirroring the Hub REST error codes. Serialized as string names via
/// <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>.
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
