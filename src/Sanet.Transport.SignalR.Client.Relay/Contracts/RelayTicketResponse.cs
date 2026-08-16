namespace Sanet.Transport.SignalR.Client.Relay.Contracts;

/// <summary>
/// Wire DTO returned by <c>POST api/rooms/{code}/relay-ticket</c>.
/// </summary>
public sealed record RelayTicketResponse(
    bool Success,
    string? Ticket,
    DateTimeOffset? ExpiresAt,
    HubError? Error);
