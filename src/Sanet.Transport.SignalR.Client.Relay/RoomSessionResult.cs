namespace Sanet.Transport.SignalR.Client.Relay;

/// <summary>
/// Result of a relay room session operation (create or join). Success carries the
/// values needed to set up the relay publisher.
/// </summary>
public sealed record RoomSessionResult(
    bool Success,
    string? RoomCode,
    string? SessionToken,
    string? Role,
    Guid? DeviceSessionId,
    Guid? HostGameId,
    RelayClientError? Error)
{
    public static RoomSessionResult Succeeded(
        string roomCode,
        string sessionToken,
        string role,
        Guid deviceSessionId,
        Guid hostGameId) =>
        new(true, roomCode, sessionToken, role, deviceSessionId, hostGameId, null);

    public static RoomSessionResult Failed(RelayClientError error) =>
        new(false, null, null, null, null, null, error);
}
