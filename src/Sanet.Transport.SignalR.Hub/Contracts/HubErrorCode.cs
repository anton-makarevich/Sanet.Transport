namespace Sanet.Transport.SignalR.Hub.Contracts;

public enum HubErrorCode
{
    HubAtCapacity,
    RoomNotFound,
    RoomExpired,
    HostNotReady,
    NotHost,
    RateLimited,
    MessageTooLarge,
    RoomFull,
    InvalidRoomState,
    MemberNotFound,
    CannotRemoveHost,
    HostDisconnected,
    ConnectionSuperseded
}
