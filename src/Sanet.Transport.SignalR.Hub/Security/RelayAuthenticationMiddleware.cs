using Microsoft.AspNetCore.Http;
using Sanet.Transport.SignalR.Hub.Rooms;

namespace Sanet.Transport.SignalR.Hub.Security;

/// <summary>
/// Validates the query-string session token for the SignalR relay hub path before the
/// WebSocket upgrade completes. Never logs credentials; only the rejection reason and
/// (for successful auth) the non-secret session identity. After successful authentication
/// the session token is removed from the request query string so it never reaches
/// downstream URL logging, tracing, or exception telemetry.
/// </summary>
public sealed class RelayAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IRoomManager roomManager,
        ILogger<RelayAuthenticationMiddleware> logger)
    {
        if (!context.Request.Path.StartsWithSegments(RelayAuthenticationDefaults.HubPath))
        {
            await next(context);
            return;
        }

        var suppliedSessionToken =
            context.Request.Query[ApiKeyAuthenticationDefaults.SessionTokenQueryParameterName];

        if (suppliedSessionToken.Count != 1 || string.IsNullOrWhiteSpace(suppliedSessionToken[0]))
        {
            logger.LogWarning(
                "Relay hub connection from {RemoteIp} rejected: session token missing or invalid",
                context.Connection.RemoteIpAddress);
            RejectUnauthorized(context);
            return;
        }

        var session = roomManager.AuthenticateSession(suppliedSessionToken[0]!);
        if (session is null)
        {
            logger.LogWarning(
                "Relay hub connection from {RemoteIp} rejected: session token not recognized",
                context.Connection.RemoteIpAddress);
            RejectUnauthorized(context);
            return;
        }

        logger.LogInformation(
            "Relay hub connection from {RemoteIp} authenticated for device session {DeviceSessionId} in room {RoomCode} as {Role}",
            context.Connection.RemoteIpAddress,
            session.DeviceSessionId,
            session.RoomCode,
            session.Role);

        context.Items[RelayAuthenticationDefaults.AuthenticatedSessionItemKey] = session;
        RemoveSessionTokenFromQueryString(context);
        await next(context);
    }

    private static void RemoveSessionTokenFromQueryString(HttpContext context)
    {
        context.Request.QueryString = QueryString.Create(
            context.Request.Query.Where(pair => !pair.Key.Equals(
                ApiKeyAuthenticationDefaults.SessionTokenQueryParameterName,
                StringComparison.OrdinalIgnoreCase)));
    }

    private static void RejectUnauthorized(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.CacheControl = "no-store";
    }
}
