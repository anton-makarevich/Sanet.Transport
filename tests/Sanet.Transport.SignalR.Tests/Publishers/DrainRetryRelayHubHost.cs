using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Sanet.Transport.SignalR.Tests.Publishers;

public sealed class DrainInvocationCounter
{
    private int _count;
    public int Increment() => Interlocked.Increment(ref _count);
    public int Count => Volatile.Read(ref _count);
}

public sealed class DrainGate
{
    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _release.TrySetResult(true);

    public Task WaitAsync() => _release.Task;
}

/// <summary>
/// Hub that aborts only the first connection (to trigger SignalR auto-reconnect),
/// throws on the first <see cref="FailUntilRelayCount"/> calls to <c>Relay</c> across
/// all hub instances, and then blocks subsequent calls until <see cref="DrainGate.Release"/>
/// is invoked. This lets a test observe publishes made while the post-reconnect drain
/// is still retrying.
/// </summary>
public sealed class DrainRetryRelayHub(
    ConnectionCounter connectionCounter,
    DrainInvocationCounter relayCounter,
    DrainGate drainGate) : Hub
{
    public const int FailUntilRelayCount = 6;

    public async Task Relay(string roomCode, object envelope)
    {
        var count = relayCounter.Increment();
        if (count <= FailUntilRelayCount)
        {
            throw new InvalidOperationException($"Simulated transient failure (attempt {count})");
        }

        await drainGate.WaitAsync();
        await Clients.Caller.SendAsync("OnReceive", envelope);
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

internal static class DrainRetryRelayHubHost
{
    public static async Task<WebApplication> StartAsync(string requiredRelayTicket)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<ConnectionCounter>();
        builder.Services.AddSingleton<DrainInvocationCounter>();
        builder.Services.AddSingleton<DrainGate>();
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
        app.MapHub<DrainRetryRelayHub>("/hubs/relay");
        await app.StartAsync();
        return app;
    }
}

