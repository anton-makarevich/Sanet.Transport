namespace Sanet.Transport.SignalR.Hub.Rooms;

public sealed record RoomLockResult(RoomLockOutcome Outcome)
{
    public static RoomLockResult Locked() =>
        new(RoomLockOutcome.Locked);

    public static RoomLockResult NotFound() =>
        new(RoomLockOutcome.RoomNotFound);

    public static RoomLockResult Expired() =>
        new(RoomLockOutcome.RoomExpired);

    public static RoomLockResult NotHost() =>
        new(RoomLockOutcome.NotHost);

    public static RoomLockResult InvalidState() =>
        new(RoomLockOutcome.InvalidRoomState);
}

public enum RoomLockOutcome
{
    Locked,
    RoomNotFound,
    RoomExpired,
    NotHost,
    InvalidRoomState
}
