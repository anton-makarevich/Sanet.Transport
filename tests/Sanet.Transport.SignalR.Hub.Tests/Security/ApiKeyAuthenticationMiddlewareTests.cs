using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sanet.Transport.SignalR.Hub.Configuration;
using Sanet.Transport.SignalR.Hub.Security;
using Sanet.Transport.SignalR.Hub.Tests.TestLoggers;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests.Security;

public class ApiKeyAuthenticationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenNoApiKeyConfigured_ReturnsUnauthorizedWithoutCallingNext()
    {
        var logger = new CapturingLogger<ApiKeyAuthenticationMiddleware>();
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        var middleware = new ApiKeyAuthenticationMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/rooms";

        await middleware.InvokeAsync(
            context,
            Options.Create(new HubOptions { ApiKey = string.Empty }),
            logger);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
        nextCalled.ShouldBeFalse();
        logger.GetMessages(LogLevel.Warning).ShouldContain(
            message => message.Contains("no API key is configured", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvokeAsync_WithValidApiKey_CallsNext()
    {
        var logger = new CapturingLogger<ApiKeyAuthenticationMiddleware>();
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        var middleware = new ApiKeyAuthenticationMiddleware(next);
        var context = new DefaultHttpContext
        {
            Request =
            {
                Method = "POST",
                Path = "/api/rooms",
                Headers =
                {
                    [ApiKeyAuthenticationDefaults.HeaderName] = "secret"
                }
            }
        };

        await middleware.InvokeAsync(
            context,
            Options.Create(new HubOptions { ApiKey = "secret" }),
            logger);

        nextCalled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }
}
