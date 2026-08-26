using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Sanet.Transport.SignalR.Tests.Publishers;

/// <summary>
/// Hub that aborts connections presenting <see cref="RebuildHubConfig.AbortTicket"/>
/// (triggering a manual ticket-refresh rebuild) and throws on the first
/// <see cref="FailUntilRelayCount"/> calls to <c>Relay</c> across all hub instances.
/// This lets a test exercise the post-rebuild queue drain when Relay invocations fail
/// while the replacement connection stays connected.
/// </summary>
internal sealed class RebuildDrainRetryRelayHub(
    RebuildHubConfig rebuildConfig,
    DrainInvocationCounter relayCounter) : Hub
{
    public const int FailUntilRelayCount = 3;

    public async Task Relay(string roomCode, object envelope)
    {
        var count = relayCounter.Increment();
        if (count <= FailUntilRelayCount)
        {
            throw new InvalidOperationException($"Simulated transient failure (attempt {count})");
        }

        await Clients.Caller.SendAsync("OnReceive", envelope);
    }

    public override Task OnConnectedAsync()
    {
        if (Context.GetHttpContext() is { } httpContext &&
            httpContext.Request.Query["ticket"].ToString() == rebuildConfig.AbortTicket)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                httpContext.Abort();
            });
        }

        return base.OnConnectedAsync();
    }
}

internal static class RebuildDrainRetryRelayHubHost
{
    public static async Task<WebApplication> StartAsync(string[] validRelayTickets, string abortTicket)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        builder.Services.AddSingleton(new RebuildHubConfig(validRelayTickets, abortTicket));
        builder.Services.AddSingleton<DrainInvocationCounter>();
        var app = builder.Build();
        app.UseWebSockets();
        app.Use(async (context, next) =>
        {
            var config = context.RequestServices.GetRequiredService<RebuildHubConfig>();
            if (!config.ValidTickets.Contains(context.Request.Query["ticket"].ToString()))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });
        app.MapHub<RebuildDrainRetryRelayHub>("/hubs/relay");
        await app.StartAsync();
        return app;
    }
}
