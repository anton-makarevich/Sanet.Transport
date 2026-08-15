namespace Sanet.Transport.SignalR.Client.Relay.Contracts;

/// <summary>
/// Wire DTO returned by <c>POST api/rooms/{code}/close</c>.
/// </summary>
public sealed record CloseResponse(
    bool Success,
    HubError? Error);
