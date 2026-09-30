namespace Sanet.Transport.Relay.Contracts;

/// <summary>
/// Filter for listing rooms a player can join (<c>GET api/rooms</c>). Only rooms whose
/// <see cref="RoomGameInfo"/> matches every supplied value are listed: the game title is
/// required; the version and the metadata entries are optional. Comparisons are exact and
/// ordinal, and all supplied metadata entries must match (AND semantics).
/// </summary>
/// <param name="GameId">Required game title identifier (<see cref="RoomGameInfo.Id"/>).</param>
/// <param name="Version">Optional game version filter (<see cref="RoomGameInfo.Version"/>).</param>
/// <param name="Metadata">
/// Optional game-specific attribute filters. Every entry must be present in the room's
/// metadata with an ordinal-equal value.
/// </param>
public sealed record RoomListFilter(
    string GameId,
    string? Version = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
