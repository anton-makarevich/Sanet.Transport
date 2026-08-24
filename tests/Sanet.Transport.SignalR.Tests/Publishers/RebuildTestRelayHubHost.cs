using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Sanet.Transport.SignalR.Tests.Publishers;

internal sealed record RebuildHubConfig(string[] ValidTickets, string AbortTicket);

/// <summary>
/// Hub that accepts connections whose ticket query parameter is one of the configured
/// valid relay tickets. Connections presenting <see cref="RebuildHubConfig.AbortTicket"/>
/// are aborted shortly after being established, simulating an unexpected mid-session
/// transport drop; connections with any other valid ticket stay connected, so a client
/// that obtains a fresh ticket and reconnects survives.
/// </summary>
public sealed class RebuildTestRelayHub : Hub
{
    public Task Relay(string roomCode, object envelope) =>
        Clients.Caller.SendAsync("OnReceive", envelope);

    public override Task OnConnectedAsync()
    {
        if (Context.GetHttpContext() is { } httpContext)
        {
            var config = httpContext.RequestServices.GetRequiredService<RebuildHubConfig>();
            var ticket = httpContext.Request.Query["ticket"].ToString();
            if (ticket == config.AbortTicket)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    httpContext.Abort();
                });
            }
        }

        return base.OnConnectedAsync();
    }
}

internal static class RebuildTestRelayHubHost
{
    public static async Task<WebApplication> StartAsync(
        string[] validRelayTickets,
        string abortTicket)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        builder.Services.AddSingleton(new RebuildHubConfig(validRelayTickets, abortTicket));
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
        app.MapHub<RebuildTestRelayHub>("/hubs/relay");
        await app.StartAsync();
        return app;
    }
}
