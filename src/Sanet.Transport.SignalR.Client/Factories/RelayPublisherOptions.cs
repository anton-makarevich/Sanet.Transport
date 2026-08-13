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
    /// The 6-character room code.
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
