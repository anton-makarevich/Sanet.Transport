using Sanet.Transport.SignalR.Client.Relay;
using HubError = Sanet.Transport.SignalR.Hub.Contracts.HubError;

namespace Sanet.Transport.SignalR.Hub.Relay;

/// <summary>
/// Client-callback contract for relay fan-out. Hub methods live on <see cref="RelayHub"/>.
/// </summary>
public interface IRelayHub
{
    Task OnReceive(RelayEnvelope message);
    Task OnPeerConnected(string peerId);
    Task OnPeerDisconnected(string peerId);
    Task OnError(HubError error);
}
