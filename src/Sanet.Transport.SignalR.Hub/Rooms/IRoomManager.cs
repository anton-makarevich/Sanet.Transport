namespace Sanet.Transport.SignalR.Hub.Rooms;

/// <summary>
/// Manages the in-memory lifecycle of relay rooms.
/// Membership identities are Hub-minted device sessions; the Hub never deals in player identity.
/// </summary>
public interface IRoomManager
{
    RoomCreationResult CreateRoom(Guid hostGameId);
    RoomJoinResult JoinRoom(string roomCode, string? sessionToken);
    RoomReadyResult MarkRoomReady(string roomCode, string sessionToken);
    RoomCloseResult CloseRoom(string roomCode, string sessionToken);
    RoomRemoveMemberResult RemoveMember(string roomCode, string sessionToken, Guid targetDeviceSessionId);
    string? RegisterConnection(string roomCode, Guid deviceSessionId, string connectionId);
    bool UnregisterConnection(string roomCode, Guid deviceSessionId, string connectionId);
    string? GetHostConnectionId(string roomCode);
    string? GetConnectionId(string roomCode, Guid deviceSessionId);
    bool TryMarkHostDisconnected(string roomCode, Guid deviceSessionId, string connectionId);
    void MarkRoomForDissolution(string roomCode);
    void CancelRoomDissolution(string roomCode);

    /// <summary>
    /// Validates a session token for any role and returns the bound session when usable for relay.
    /// Returns null for missing, unknown, expired, revoked, dissolved, or room-mismatched tokens.
    /// </summary>
    RoomSession? AuthenticateSession(string sessionToken);

    /// <summary>
    /// Issues a short-lived relay ticket bound to the session identified by
    /// <paramref name="sessionToken"/> in the room <paramref name="roomCode"/>.
    /// The ticket authenticates SignalR hub connections without exposing the session token.
    /// </summary>
    RelayTicketResult IssueRelayTicket(string roomCode, string sessionToken);

    /// <summary>
    /// Resolves a relay ticket to its bound session. Returns null for unknown, expired,
    /// revoked, or room-dissolved tickets. A ticket may be redeemed more than once while
    /// unexpired so reconnects within the ticket window keep working.
    /// </summary>
    RoomSession? RedeemRelayTicket(string ticket);
}
