namespace Sanet.Transport.Relay.Contracts;

/// <summary>
/// Game identity and game-specific attributes of a relay room, reported by the host when the
/// room is created. The same Relay hub can serve different games, so every room carries the
/// game title, the game version, and the metadata that is meaningful only to that game.
/// No player identity is accepted at the Hub boundary.
/// </summary>
/// <param name="HostId">
/// Id of the host's game instance, reported by the host at room creation. It must not be
/// <see cref="Guid.Empty"/>. This identifies the game, not a device: it is neither a game title
/// nor a player identity, and it is deliberately separate from the Hub-minted device session id.
/// </param>
/// <param name="Id">
/// Game title identifier, for example <c>"MakaMek"</c>. Many rooms share the same value, so it
/// is not unique per room. Must not be blank, must be at most
/// <see cref="RoomGameInfoLimits.MaxIdLength"/> characters long, and must contain only letters,
/// digits, <c>'.'</c>, <c>'_'</c> and <c>'-'</c> so it stays safe for query-string filtering.
/// </param>
/// <param name="Version">
/// Game version reported by the host, for example <c>"v0.64.0"</c>. Must not be blank, must be at
/// most <see cref="RoomGameInfoLimits.MaxVersionLength"/> characters long, and must not carry
/// leading or trailing whitespace or control characters.
/// </param>
/// <param name="Metadata">
/// Game-specific attributes, such as rules, map, or tech level. The Hub treats a missing or
/// <c>null</c> value as an empty dictionary. Keys must not be blank, are limited to
/// <see cref="RoomGameInfoLimits.MaxMetadataKeyLength"/> characters, and are compared with
/// ordinal (case-sensitive) semantics, so <c>"techLevel"</c> and <c>"techlevel"</c> are
/// different keys. At most <see cref="RoomGameInfoLimits.MaxMetadataEntries"/> entries are
/// accepted, each value at most <see cref="RoomGameInfoLimits.MaxMetadataValueLength"/>
/// characters long.
/// </param>
/// <example>
/// { "hostId": "3f2c0a1e-5b6d-4f70-8a91-2c3d4e5f6a7b", "id": "MakaMek", "version": "v0.64.0", "metadata": { "rules": "standard", "techLevel": "introductory" } }
/// </example>
public sealed record RoomGameInfo(
    Guid HostId,
    string Id,
    string Version,
    IReadOnlyDictionary<string, string>? Metadata = null);
