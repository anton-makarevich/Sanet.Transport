namespace Sanet.Transport.Relay.Contracts;

/// <summary>
/// Result of creating a relay room (<c>POST api/rooms</c>). Carries the Hub-minted
/// device session identity and the stored <see cref="RoomGameInfo"/> whose
/// <see cref="RoomGameInfo.HostId"/> is the host game instance id and whose
/// <see cref="RoomGameInfo.Id"/> is the game title; never carries player identity.
/// </summary>
/// <param name="Success">True when the room was created.</param>
/// <param name="RoomCode">Code of the created room, or null on failure.</param>
/// <param name="DeviceSessionId">Hub-minted device session id of the host, or null on failure.</param>
/// <param name="GameInfo">Game identity and attributes of the created room, or null on failure.</param>
/// <param name="SessionToken">Session token of the host, or null on failure.</param>
/// <param name="ExpiresAt">Expiry of the created room, or null on failure.</param>
/// <param name="Error">Failure detail, or null on success.</param>
public sealed record CreateRoomResponse(
    bool Success,
    string? RoomCode,
    Guid? DeviceSessionId,
    RoomGameInfo? GameInfo,
    string? SessionToken,
    DateTimeOffset? ExpiresAt,
    HubError? Error);
