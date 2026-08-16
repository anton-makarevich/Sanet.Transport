namespace Sanet.Transport.SignalR.Hub.Rooms;

/// <summary>
/// Result of issuing a short-lived relay ticket for a room session.
/// Success carries the ticket value and its expiry.
/// </summary>
public sealed record RelayTicketResult(
    RelayTicketOutcome Outcome,
    string? Ticket,
    DateTimeOffset? ExpiresAt)
{
    public static RelayTicketResult Issued(string ticket, DateTimeOffset expiresAt) =>
        new(RelayTicketOutcome.Issued, ticket, expiresAt);

    public static RelayTicketResult NotFound() =>
        new(RelayTicketOutcome.RoomNotFound, null, null);

    public static RelayTicketResult Expired() =>
        new(RelayTicketOutcome.RoomExpired, null, null);

    public static RelayTicketResult SessionInvalid() =>
        new(RelayTicketOutcome.SessionInvalid, null, null);

    public static RelayTicketResult LimitReached() =>
        new(RelayTicketOutcome.LimitReached, null, null);
}

public enum RelayTicketOutcome
{
    Issued,
    RoomNotFound,
    RoomExpired,
    SessionInvalid,
    LimitReached
}
