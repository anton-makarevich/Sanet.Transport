namespace Sanet.Transport.Relay.Contracts;

/// <summary>
/// Summary of a room a player can join, as returned by listing (<c>GET api/rooms</c>).
/// Carries the room code, the host-supplied <see cref="RoomGameInfo"/> whose
/// <see cref="RoomGameInfo.HostId"/> is the host game instance id and whose
/// <see cref="RoomGameInfo.Id"/> is the game title, plus basic room facts. It never carries
/// session tokens, device session ids, or connection routing.
/// </summary>
/// <param name="RoomCode">Code used to join the room.</param>
/// <param name="CreatedAt">Time the room was created.</param>
/// <param name="MemberCount">Number of device sessions currently in the room.</param>
/// <param name="GameInfo">Game identity and attributes of the room.</param>
public sealed record RoomSummary(
    string RoomCode,
    DateTimeOffset CreatedAt,
    int MemberCount,
    RoomGameInfo GameInfo);
