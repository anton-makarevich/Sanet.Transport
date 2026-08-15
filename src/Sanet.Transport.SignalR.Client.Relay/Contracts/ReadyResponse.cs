namespace Sanet.Transport.SignalR.Client.Relay.Contracts;

/// <summary>
/// Wire DTO returned by <c>POST api/rooms/{code}/ready</c>.
/// </summary>
public sealed record ReadyResponse(
    bool Success,
    HubError? Error);
