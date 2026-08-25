using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Sanet.Transport.SignalR.Tests.Publishers;

public sealed class ConnectionCounter
{
    private int _count;
    public int Increment() => Interlocked.Increment(ref _count);
}

/// <summary>
/// Hub that aborts only the first connection (to trigger SignalR auto-reconnect),
/// then keeps subsequent connections alive. Supports <c>Relay</c> for publish/receive
/// testing during the auto-reconnect drain window.
/// </summary>
public sealed class AutoReconnectTestRelayHub(ConnectionCounter counter) : Hub
{
    public Task Relay(string roomCode, object envelope) =>
        Clients.Caller.SendAsync("OnReceive", envelope);

    public override Task OnConnectedAsync()
    {
        if (counter.Increment() == 1 && Context.GetHttpContext() is { } httpContext)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                httpContext.Abort();
            });
        }

        return base.OnConnectedAsync();
    }
}

internal static class AutoReconnectTestRelayHubHost
{
    public static async Task<WebApplication> StartAsync(string requiredRelayTicket)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<ConnectionCounter>();
        var app = builder.Build();
        app.UseWebSockets();
        app.Use(async (context, next) =>
        {
            if (context.Request.Query["ticket"].ToString() != requiredRelayTicket)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });
        app.MapHub<AutoReconnectTestRelayHub>("/hubs/relay");
        await app.StartAsync();
        return app;
    }
}
