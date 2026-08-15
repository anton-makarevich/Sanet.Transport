using Microsoft.AspNetCore.Http;
using NSubstitute;
using Sanet.Transport.SignalR.Hub.Rooms;
using Sanet.Transport.SignalR.Hub.Security;
using Sanet.Transport.SignalR.Hub.Tests.TestLoggers;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests.Security;

public class RelayAuthenticationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WithValidSessionToken_RemovesTokenFromQueryStringBeforeNext()
    {
        const string sessionToken = "secret-token";
        var roomManager = Substitute.For<IRoomManager>();
        roomManager.AuthenticateSession(Arg.Any<string>()).Returns(
            new RoomSession(sessionToken, "ROOM01", Guid.NewGuid(), RoomRole.Host, DateTimeOffset.UtcNow.AddHours(1)));

        HttpContext? seenContext = null;
        RequestDelegate next = context =>
        {
            seenContext = context;
            return Task.CompletedTask;
        };

        var middleware = new RelayAuthenticationMiddleware(next);
        var context = new DefaultHttpContext
        {
            Request =
            {
                Path = RelayAuthenticationDefaults.HubPath,
                QueryString = new QueryString($"?foo=bar&sessionToken={sessionToken}")
            }
        };

        await middleware.InvokeAsync(
            context,
            roomManager,
            new CapturingLogger<RelayAuthenticationMiddleware>());

        seenContext.ShouldNotBeNull();
        seenContext!.Request.QueryString.HasValue.ShouldBeTrue();
        seenContext.Request.Query.ContainsKey(ApiKeyAuthenticationDefaults.SessionTokenQueryParameterName).ShouldBeFalse();
        seenContext.Request.Query["foo"].ToString().ShouldBe("bar");
    }

    [Fact]
    public async Task InvokeAsync_WithSessionTokenOnlyQuery_RemovesTokenLeavingEmptyQuery()
    {
        const string sessionToken = "secret-token";
        var roomManager = Substitute.For<IRoomManager>();
        roomManager.AuthenticateSession(Arg.Any<string>()).Returns(
            new RoomSession(sessionToken, "ROOM01", Guid.NewGuid(), RoomRole.Host, DateTimeOffset.UtcNow.AddHours(1)));

        HttpContext? seenContext = null;
        RequestDelegate next = context =>
        {
            seenContext = context;
            return Task.CompletedTask;
        };

        var middleware = new RelayAuthenticationMiddleware(next);
        var context = new DefaultHttpContext
        {
            Request =
            {
                Path = RelayAuthenticationDefaults.HubPath,
                QueryString = new QueryString($"?sessionToken={sessionToken}")
            }
        };

        await middleware.InvokeAsync(
            context,
            roomManager,
            new CapturingLogger<RelayAuthenticationMiddleware>());

        seenContext.ShouldNotBeNull();
        seenContext!.Request.QueryString.HasValue.ShouldBeFalse();
        seenContext.Request.Query.Any().ShouldBeFalse();
    }
}
