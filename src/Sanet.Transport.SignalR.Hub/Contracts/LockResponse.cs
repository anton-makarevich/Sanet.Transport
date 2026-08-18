namespace Sanet.Transport.SignalR.Hub.Contracts;

public sealed record LockResponse(
    bool Success,
    HubError? Error);
