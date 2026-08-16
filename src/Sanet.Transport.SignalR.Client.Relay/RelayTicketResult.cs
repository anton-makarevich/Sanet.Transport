namespace Sanet.Transport.SignalR.Client.Relay;

/// <summary>
/// Result of requesting a short-lived relay ticket for a room session. Success carries the
/// ticket value and its expiry, which are needed to build the SignalR relay hub URL.
/// </summary>
public sealed record RelayTicketResult(
    bool Success,
    string? Ticket,
    DateTimeOffset? ExpiresAt,
    RelayClientError? Error)
{
    public static RelayTicketResult Succeeded(string ticket, DateTimeOffset expiresAt) =>
        new(true, ticket, expiresAt, null);

    public static RelayTicketResult Failed(RelayClientError error) =>
        new(false, null, null, error);
}
