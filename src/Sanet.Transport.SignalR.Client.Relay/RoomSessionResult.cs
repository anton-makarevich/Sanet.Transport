using Sanet.Transport.Relay.Contracts;

namespace Sanet.Transport.SignalR.Client.Relay;

/// <summary>
/// Result of a relay room session operation (create or join). Success carries the
/// values needed to set up the relay publisher.
/// </summary>
/// <param name="Success">True when the operation succeeded.</param>
/// <param name="RoomCode">Code of the room, or null on failure.</param>
/// <param name="SessionToken">Session token of the device, or null on failure.</param>
/// <param name="Role">Role assigned to the device, or null on failure.</param>
/// <param name="DeviceSessionId">Hub-minted device session id, or null on failure.</param>
/// <param name="GameInfo">
/// Game identity and attributes of the room, echoed by the Hub. Its
/// <see cref="RoomGameInfo.HostId"/> is the host game instance id and its
/// <see cref="RoomGameInfo.Id"/> is the game title, so a joiner can check compatibility.
/// Null on failure.
/// </param>
/// <param name="Error">Failure detail, or null on success.</param>
public sealed record RoomSessionResult(
    bool Success,
    string? RoomCode,
    string? SessionToken,
    string? Role,
    Guid? DeviceSessionId,
    RoomGameInfo? GameInfo,
    RelayClientError? Error)
{
    public static RoomSessionResult Succeeded(
        string roomCode,
        string sessionToken,
        string role,
        Guid deviceSessionId,
        RoomGameInfo gameInfo) =>
        new(true, roomCode, sessionToken, role, deviceSessionId, gameInfo, null);

    public static RoomSessionResult Failed(RelayClientError error) =>
        new(false, null, null, null, null, null, error);
}
