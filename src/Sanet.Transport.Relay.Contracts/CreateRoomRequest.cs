namespace Sanet.Transport.Relay.Contracts;

/// <summary>
/// Identifies the game a relay room is created for. The room requires
/// <see cref="RoomGameInfo"/>: <see cref="RoomGameInfo.HostId"/> is the host game instance id and
/// <see cref="RoomGameInfo.Id"/> is the game title. No player identity is accepted at the Hub
/// boundary. Wire body of <c>POST api/rooms</c>.
/// </summary>
public sealed record CreateRoomRequest(RoomGameInfo GameInfo);
