using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Sanet.Transport.SignalR.Tests.Publishers;

public sealed class RelayInvocationCounter
{
    private int _count;
    public int Increment() => Interlocked.Increment(ref _count);
    public int Count => Volatile.Read(ref _count);
}

/// <summary>
/// Hub that aborts only the first connection (to trigger SignalR auto-reconnect)
/// and throws on the first N calls to <c>Relay</c> across all hub instances
/// (via a shared <see cref="RelayInvocationCounter"/>), simulating transient invocation
/// failures during the post-reconnect queue drain. Subsequent calls succeed.
/// </summary>
public sealed class FailInvocationRelayHub(
    ConnectionCounter connectionCounter,
    RelayInvocationCounter relayCounter) : Hub
{
    private const int FailUntilRelayCount = 2;

    public Task Relay(string roomCode, object envelope)
    {
        var count = relayCounter.Increment();
        if (count <= FailUntilRelayCount)
        {
            throw new InvalidOperationException($"Simulated transient failure (attempt {count})");
        }
        return Clients.Caller.SendAsync("OnReceive", envelope);
    }

    public override Task OnConnectedAsync()
    {
        if (connectionCounter.Increment() == 1 && Context.GetHttpContext() is { } httpContext)
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

internal static class FailInvocationRelayHubHost
{
    public static async Task<WebApplication> StartAsync(string requiredRelayTicket)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<ConnectionCounter>();
        builder.Services.AddSingleton<RelayInvocationCounter>();
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
        app.MapHub<FailInvocationRelayHub>("/hubs/relay");
        await app.StartAsync();
        return app;
    }
}
