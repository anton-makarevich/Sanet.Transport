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
    /// The session token issued by the REST room join/create API.
    /// </summary>
    public required string SessionToken { get; init; }

    /// <summary>
    /// Optional API key appended as a query parameter. Required by hubs
    /// that enforce relay authentication (RelayAuthenticationMiddleware).
    /// </summary>
    public string? ApiKey { get; init; }
}
