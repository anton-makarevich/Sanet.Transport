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
}
