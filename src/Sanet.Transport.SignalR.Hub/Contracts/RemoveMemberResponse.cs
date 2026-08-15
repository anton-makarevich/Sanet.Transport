namespace Sanet.Transport.SignalR.Hub.Contracts;

public sealed record RemoveMemberResponse(
    bool Success,
    HubError? Error);
