using Sanet.Transport.Relay.Contracts;

namespace Sanet.Transport.SignalR.Client.Relay;

/// <summary>
/// Result of listing rooms a player can join. Success carries a read-only snapshot of the
/// matching rooms; failure carries the error and an empty list.
/// </summary>
/// <param name="Success">True when the listing succeeded.</param>
/// <param name="Rooms">
/// Rooms a player can join, or an empty list on failure. A successful empty list means no
/// room matched the filter.
/// </param>
/// <param name="Error">Failure detail, or null on success.</param>
public sealed record RoomListResult(
    bool Success,
    IReadOnlyList<RoomSummary> Rooms,
    RelayClientError? Error)
{
    public static RoomListResult Succeeded(IReadOnlyList<RoomSummary> rooms) =>
        new(true, rooms, null);

    public static RoomListResult Failed(RelayClientError error) =>
        new(false, [], error);
}
