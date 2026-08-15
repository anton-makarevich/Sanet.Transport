namespace Sanet.Transport.SignalR.Client.Relay.Contracts;

/// <summary>
/// Wire DTO for <c>POST api/rooms</c>. Carries only the host game id.
/// </summary>
public sealed record CreateRoomRequest(Guid GameId);
