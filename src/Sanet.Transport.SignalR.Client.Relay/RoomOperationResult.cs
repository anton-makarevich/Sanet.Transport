namespace Sanet.Transport.SignalR.Client.Relay;

/// <summary>
/// Generic ack result for Ready, Lock, and RemoveMember operations.
/// </summary>
public sealed record RoomOperationResult(
    bool Success,
    RelayClientError? Error)
{
    public static RoomOperationResult Succeeded() => new(true, null);

    public static RoomOperationResult Failed(RelayClientError error) => new(false, error);
}
