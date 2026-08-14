namespace Sanet.Transport.SignalR.Hub.Rooms;

/// <summary>
/// Opaque session credentials bound to one device session.
/// </summary>
public sealed record RoomSession(
    string Token,
    string RoomCode,
    Guid DeviceSessionId,
    RoomRole Role,
    DateTimeOffset ExpiresAt)
{
    /// <summary>
    /// Never exposes the token, which is an opaque secret credential.
    /// </summary>
    public override string ToString()
        => $"RoomSession {{ RoomCode = {RoomCode}, DeviceSessionId = {DeviceSessionId}, Role = {Role}, ExpiresAt = {ExpiresAt} }}";
}
