namespace Sanet.Transport.SignalR.Client.Relay;

/// <summary>
/// Error returned by the relay hub.
/// </summary>
public sealed record HubError(
    HubErrorCode Code,
    string Message,
    string? RoomCode = null);
