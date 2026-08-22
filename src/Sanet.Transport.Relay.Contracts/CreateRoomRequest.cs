namespace Sanet.Transport.Relay.Contracts;

/// <summary>
/// Identifies the host's ServerGame for which a relay room is created.
/// No player identity is accepted at the Hub boundary. Wire body of <c>POST api/rooms</c>.
/// </summary>
public sealed record CreateRoomRequest(Guid GameId);
