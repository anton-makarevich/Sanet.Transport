namespace Sanet.Transport.SignalR.Hub.Rooms;

/// <summary>
/// Generates a user-shareable room code.
/// </summary>
public interface IRoomCodeGenerator
{
    string Generate();
}
