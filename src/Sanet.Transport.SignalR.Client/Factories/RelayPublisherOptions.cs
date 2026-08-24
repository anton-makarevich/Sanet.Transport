namespace Sanet.Transport.SignalR.Client.Factories;

/// <summary>
/// Options for creating a <see cref="Publishers.RelayClientPublisher"/> via <see cref="RelayPublisherFactory"/>.
/// </summary>
public sealed record RelayPublisherOptions : PublisherOptions
{
    /// <summary>
    /// The base URL of the SignalR relay hub.
    /// </summary>
    public required string HubUrl { get; init; }

    /// <summary>
    /// The room code. Must be exactly 6 characters, otherwise the constructor of
    /// <see cref="Publishers.RelayClientPublisher"/> throws an <see cref="ArgumentException"/>.
    /// </summary>
    public required string RoomCode { get; init; }

    /// <summary>
    /// The short-lived relay ticket issued by the REST room relay-ticket API. The ticket is
    /// carried in the hub URL query string; the REST session token must never be exposed there.
    /// </summary>
    public required string RelayTicket { get; init; }

    /// <summary>
    /// Optional delegate invoked by <see cref="Publishers.RelayClientPublisher"/> when the
    /// connection closes, to obtain a fresh relay ticket (typically via
    /// <c>IRelayRoomClient.GetRelayTicket</c> using the stored session token and room code)
    /// and transparently rebuild and restart the connection. When null, a closed connection
    /// raises the terminal <see cref="Publishers.RelayClientPublisher.Closed"/> event.
    /// </summary>
    public Func<CancellationToken, Task<Publishers.RelayTicketRefresh?>>? TicketRefresh { get; init; }
}
