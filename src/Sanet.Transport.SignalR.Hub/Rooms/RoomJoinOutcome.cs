namespace Sanet.Transport.SignalR.Hub.Rooms;

public enum RoomJoinOutcome
{
    Joined,
    RoomNotFound,
    RoomExpired,
    HostNotReady,
    RoomFull,
    Forbidden
}