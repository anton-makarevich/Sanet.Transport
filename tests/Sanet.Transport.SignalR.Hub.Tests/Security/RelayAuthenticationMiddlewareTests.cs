using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sanet.Transport.SignalR.Hub.Rooms;
using Sanet.Transport.SignalR.Hub.Security;
using Sanet.Transport.SignalR.Hub.Tests.TestLoggers;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests.Security;

public class RelayAuthenticationMiddlewareTests
{
    private const string RelayTicket = "relay-ticket-value";
    private const string TicketQuery = "ticket";

    private static RoomSession CreateSession(string ticket = RelayTicket) =>
        new(ticket, "ROOM01", Guid.NewGuid(), RoomRole.Host, DateTimeOffset.UtcNow.AddHours(1));

    [Fact]
    public async Task InvokeAsync_WithValidTicket_RemovesTicketFromQueryStringBeforeNext()
    {
        var roomManager = Substitute.For<IRoomManager>();
        var session = CreateSession();
        roomManager.RedeemRelayTicket(RelayTicket).Returns(session);

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
                QueryString = new QueryString($"?foo=bar&{TicketQuery}={RelayTicket}")
            }
        };

        await middleware.InvokeAsync(
            context,
            roomManager,
            new CapturingLogger<RelayAuthenticationMiddleware>());

        seenContext.ShouldNotBeNull();
        seenContext!.Request.QueryString.HasValue.ShouldBeTrue();
        seenContext.Request.Query.ContainsKey(ApiKeyAuthenticationDefaults.TicketQueryParameterName).ShouldBeFalse();
        seenContext.Request.Query["foo"].ToString().ShouldBe("bar");
    }

    [Fact]
    public async Task InvokeAsync_WithTicketOnlyQuery_RemovesTicketLeavingEmptyQuery()
    {
        var roomManager = Substitute.For<IRoomManager>();
        var session = CreateSession();
        roomManager.RedeemRelayTicket(RelayTicket).Returns(session);

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
                QueryString = new QueryString($"?{TicketQuery}={RelayTicket}")
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

    [Fact]
    public async Task InvokeAsync_WithValidTicket_StoresSessionInContextItems()
    {
        var roomManager = Substitute.For<IRoomManager>();
        var session = CreateSession();
        roomManager.RedeemRelayTicket(RelayTicket).Returns(session);

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
                QueryString = new QueryString($"?{TicketQuery}={RelayTicket}")
            }
        };

        await middleware.InvokeAsync(
            context,
            roomManager,
            new CapturingLogger<RelayAuthenticationMiddleware>());

        seenContext.ShouldNotBeNull();
        seenContext!.Items[RelayAuthenticationDefaults.AuthenticatedSessionItemKey]
            .ShouldBeSameAs(session);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvokeAsync_WithMissingOrBlankTicket_RejectsUnauthorizedWithoutCallingNext(string? ticket)
    {
        var roomManager = Substitute.For<IRoomManager>();
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new RelayAuthenticationMiddleware(next);
        var context = new DefaultHttpContext
        {
            Request =
            {
                Path = RelayAuthenticationDefaults.HubPath,
                QueryString = ticket is null
                    ? QueryString.Empty
                    : new QueryString($"?{TicketQuery}={Uri.EscapeDataString(ticket)}")
            }
        };

        await middleware.InvokeAsync(
            context,
            roomManager,
            new CapturingLogger<RelayAuthenticationMiddleware>());

        nextCalled.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        roomManager.DidNotReceive().RedeemRelayTicket(Arg.Any<string>());
    }

    [Fact]
    public async Task InvokeAsync_WithUnknownTicket_RejectsUnauthorizedWithoutLeakingTicketInLogs()
    {
        const string distinctiveTicket = "distinctive-unknown-ticket-value";
        var roomManager = Substitute.For<IRoomManager>();
        roomManager.RedeemRelayTicket(distinctiveTicket).Returns((RoomSession?)null);
        var logger = new CapturingLogger<RelayAuthenticationMiddleware>();
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new RelayAuthenticationMiddleware(next);
        var context = new DefaultHttpContext
        {
            Request =
            {
                Path = RelayAuthenticationDefaults.HubPath,
                QueryString = new QueryString($"?{TicketQuery}={distinctiveTicket}")
            }
        };

        await middleware.InvokeAsync(
            context,
            roomManager,
            logger);

        nextCalled.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        logger.GetMessages(LogLevel.Warning).ShouldContain(
            message => message.Contains("relay ticket", StringComparison.OrdinalIgnoreCase));
        logger.GetMessages(LogLevel.Warning).ShouldNotContain(
            message => message.Contains(distinctiveTicket, StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvokeAsync_OnNonHubPath_DoesNotValidateTicket()
    {
        var roomManager = Substitute.For<IRoomManager>();
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new RelayAuthenticationMiddleware(next);
        var context = new DefaultHttpContext
        {
            Request =
            {
                Path = "/api/rooms",
                QueryString = new QueryString($"?{TicketQuery}={RelayTicket}")
            }
        };

        await middleware.InvokeAsync(
            context,
            roomManager,
            new CapturingLogger<RelayAuthenticationMiddleware>());

        nextCalled.ShouldBeTrue();
        roomManager.DidNotReceive().RedeemRelayTicket(Arg.Any<string>());
    }
}
