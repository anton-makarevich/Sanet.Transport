namespace Sanet.Transport.Relay.Contracts;

/// <summary>
/// Result of listing rooms a player can join (<c>GET api/rooms</c>). An empty list is a
/// successful response when no room matches the filter.
/// </summary>
/// <param name="Rooms">Rooms a player can join, sorted by creation time ascending.</param>
public sealed record ListRoomsResponse(IReadOnlyList<RoomSummary> Rooms);
