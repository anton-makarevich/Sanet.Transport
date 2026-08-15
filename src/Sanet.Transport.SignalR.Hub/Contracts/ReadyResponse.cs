namespace Sanet.Transport.SignalR.Hub.Contracts;

public sealed record ReadyResponse(
    bool Success,
    HubError? Error);
