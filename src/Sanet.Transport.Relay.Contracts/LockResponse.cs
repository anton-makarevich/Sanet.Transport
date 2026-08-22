namespace Sanet.Transport.Relay.Contracts;

/// <summary>
/// Wire DTO returned by <c>POST api/rooms/{code}/lock</c>.
/// </summary>
public sealed record LockResponse(
    bool Success,
    HubError? Error);
