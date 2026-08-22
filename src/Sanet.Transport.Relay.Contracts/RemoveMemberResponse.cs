namespace Sanet.Transport.Relay.Contracts;

/// <summary>
/// Wire DTO returned by <c>DELETE api/rooms/{code}/members/{playerId}</c>.
/// </summary>
public sealed record RemoveMemberResponse(
    bool Success,
    HubError? Error);
