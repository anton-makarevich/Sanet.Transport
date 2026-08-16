using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Sanet.Transport.SignalR.Tests.Publishers;

/// <summary>
/// Hub that accepts connections whose ticket query parameter matches the configured
/// relay ticket, then aborts each connection shortly after it is established to force
/// an unexpected transport drop on the client.
/// </summary>
public sealed class FlakyTestRelayHub : Hub
{
    public override Task OnConnectedAsync()
    {
        if (Context.GetHttpContext() is { } httpContext)
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

internal static class FlakyTestRelayHubHost
{
    public static async Task<WebApplication> StartAsync(string requiredRelayTicket)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
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
        app.MapHub<FlakyTestRelayHub>("/hubs/relay");
        await app.StartAsync();
        return app;
    }
}
