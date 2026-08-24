namespace Sanet.Transport.SignalR.Client.Publishers;

/// <summary>
/// A freshly issued relay ticket together with its expiry, returned by the
/// <see cref="RelayClientPublisher"/> ticket-refresh delegate when the connection
/// must be rebuilt after the previous relay ticket's window has passed.
/// </summary>
/// <param name="RelayTicket">The new short-lived relay ticket issued by the REST relay-ticket API.</param>
/// <param name="ExpiresAt">The point in time at which <paramref name="RelayTicket"/> expires.</param>
public sealed record RelayTicketRefresh(string RelayTicket, DateTimeOffset ExpiresAt);
