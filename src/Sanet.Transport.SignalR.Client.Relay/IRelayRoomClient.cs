namespace Sanet.Transport.SignalR.Client.Relay;

/// <summary>
/// Typed client for the Hub REST room lifecycle (create, join, ready, close, remove member).
/// The Hub boundary deals in Hub-minted device session identities and the host game id;
/// no player identity is sent or received.
/// </summary>
/// <remarks>
/// <see cref="Create"/>, <see cref="Join"/>, <see cref="Ready"/>, <see cref="Close"/>,
/// <see cref="RemoveMember"/> and <see cref="GetRelayTicket"/> accept an optional
/// <see cref="RelayClientOptions"/> to pin a room lifecycle to the hub it was started on.
/// When omitted, the currently active hub configuration is resolved for each request.
/// </remarks>
public interface IRelayRoomClient
{
    Task<RoomSessionResult> Create(
        Guid gameId,
        CancellationToken cancellationToken = default,
        RelayClientOptions? options = null);

    Task<RoomSessionResult> Join(
        string roomCode,
        string? sessionToken,
        CancellationToken cancellationToken = default,
        RelayClientOptions? options = null);

    Task<RoomOperationResult> Ready(
        string roomCode,
        string sessionToken,
        CancellationToken cancellationToken = default,
        RelayClientOptions? options = null);

    Task<RoomOperationResult> Close(
        string roomCode,
        string sessionToken,
        CancellationToken cancellationToken = default,
        RelayClientOptions? options = null);

    Task<RoomOperationResult> RemoveMember(
        string roomCode,
        string sessionToken,
        Guid deviceSessionId,
        CancellationToken cancellationToken = default,
        RelayClientOptions? options = null);

    /// <summary>
    /// Requests a short-lived relay ticket for the given room session. The ticket is used to
    /// authenticate the SignalR relay hub connection via the hub URL query string; the REST
    /// session token must never appear in the URL.
    /// </summary>
    Task<RelayTicketResult> GetRelayTicket(
        string roomCode,
        string sessionToken,
        CancellationToken cancellationToken = default,
        RelayClientOptions? options = null);

    /// <summary>
    /// Probes the relay hub health endpoint. Returns <c>null</c> when the hub is reachable,
    /// or a client error describing why it is not.
    /// </summary>
    Task<RelayClientError?> Health(
        CancellationToken cancellationToken = default,
        RelayClientOptions? options = null);
}
