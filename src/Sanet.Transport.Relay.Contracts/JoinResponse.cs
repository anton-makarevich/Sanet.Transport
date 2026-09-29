namespace Sanet.Transport.Relay.Contracts;

/// <summary>
/// Result of joining a relay room (<c>POST api/rooms/{code}/join</c>). Carries the
/// Hub-minted device session identity and the room's <see cref="RoomGameInfo"/> whose
/// <see cref="RoomGameInfo.HostId"/> is the host game instance id and whose
/// <see cref="RoomGameInfo.Id"/> is the game title, so a joiner can check compatibility;
/// never carries player identity.
/// </summary>
/// <param name="Success">True when the device joined the room.</param>
/// <param name="Role">Role assigned to the joining device, or null on failure.</param>
/// <param name="DeviceSessionId">Hub-minted device session id, or null on failure.</param>
/// <param name="GameInfo">Game identity and attributes of the room, or null on failure.</param>
/// <param name="SessionToken">Session token of the joining device, or null on failure.</param>
/// <param name="Error">Failure detail, or null on success.</param>
public sealed record JoinResponse(
    bool Success,
    string? Role,
    Guid? DeviceSessionId,
    RoomGameInfo? GameInfo,
    string? SessionToken,
    HubError? Error);
