using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Sanet.Transport.SignalR.Tests.Publishers;

/// <summary>
/// Hub with no ticket requirement that aborts only the first connection
/// (to trigger SignalR auto-reconnect), then keeps subsequent connections alive.
/// </summary>
public sealed class LanTestHub(ConnectionCounter counter) : Hub
{
    public Task Relay(string roomCode, object envelope) =>
        Clients.Caller.SendAsync("OnReceive", envelope);

    public override Task OnConnectedAsync()
    {
        if (counter.Increment() == 1 && Context.GetHttpContext() is { } httpContext)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400));
                httpContext.Abort();
            });
        }

        return base.OnConnectedAsync();
    }
}

internal static class LanTestHubHost
{
    public static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<ConnectionCounter>();
        var app = builder.Build();
        app.UseWebSockets();
        app.MapHub<LanTestHub>("/hubs/lan");
        await app.StartAsync();
        return app;
    }
}