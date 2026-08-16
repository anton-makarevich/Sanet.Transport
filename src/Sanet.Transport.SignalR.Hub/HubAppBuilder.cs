using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Sanet.Transport.SignalR.Hub.Configuration;
using Sanet.Transport.SignalR.Hub.Contracts;
using Sanet.Transport.SignalR.Hub.Relay;
using Sanet.Transport.SignalR.Hub.Rooms;
using Sanet.Transport.SignalR.Hub.Security;

namespace Sanet.Transport.SignalR.Hub;

/// <summary>
/// Builds the relay hub <see cref="WebApplication"/>. The host process simply runs it;
/// integration tests call <see cref="CreateApp"/> directly to boot an identical app over a
/// real Kestrel listener.
/// </summary>
public static class HubAppBuilder
{
    /// <summary>
    /// Creates the fully-configured relay hub application.
    /// </summary>
    /// <param name="args">Command-line arguments forwarded to the configuration system.</param>
    /// <param name="options">Optional application options; tests use this to override application identity.</param>
    /// <param name="configureBuilder">Optional hook to adjust services and configuration before the app is built.</param>
    public static WebApplication CreateApp(
        string[] args,
        WebApplicationOptions? options = null,
        Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var builder = options is null
            ? WebApplication.CreateBuilder(args)
            : WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = options.ApplicationName,
                ContentRootPath = options.ContentRootPath,
                EnvironmentName = options.EnvironmentName,
                WebRootPath = options.WebRootPath,
                Args = args
            });

        // Single-line console output with timestamps so room lifecycle, connections and relay
        // traffic are easy to follow while debugging the relay locally.
        builder.Logging.AddSimpleConsole(loggingOptions =>
        {
            loggingOptions.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
            loggingOptions.SingleLine = true;
        });

        builder.Services
            .AddOptions<HubOptions>()
            .Bind(builder.Configuration.GetSection(HubOptions.SectionName))
            .Validate(
                o => o.MaxConcurrentRooms > 0,
                $"{HubOptions.SectionName}:MaxConcurrentRooms must be greater than zero.")
            .Validate(
                o => o.JoinRateLimitPerMinute > 0,
                $"{HubOptions.SectionName}:JoinRateLimitPerMinute must be greater than zero.")
            .Validate(
                o => o.RelayRateLimitPerMinute > 0,
                $"{HubOptions.SectionName}:RelayRateLimitPerMinute must be greater than zero.")
            .Validate(
                o => o.MaxRelayPayloadBytes > 0,
                $"{HubOptions.SectionName}:MaxRelayPayloadBytes must be greater than zero.")
            .Validate(
                o => o.RoomTtlSeconds > 0,
                $"{HubOptions.SectionName}:RoomTtlSeconds must be greater than zero.")
            .Validate(
                o => o.DissolutionGracePeriodSeconds > 0,
                $"{HubOptions.SectionName}:DissolutionGracePeriodSeconds must be greater than zero.")
            .Validate(
                o => o.RelayTicketTtlSeconds > 0,
                $"{HubOptions.SectionName}:RelayTicketTtlSeconds must be greater than zero.")
            .Validate(
                o => o.PeerDisconnectNotificationDelaySeconds >= 0,
                $"{HubOptions.SectionName}:PeerDisconnectNotificationDelaySeconds must be greater than or equal to zero.")
            .Validate(
                o => o.SignalR.KeepAliveIntervalSeconds > 0,
                $"{HubOptions.SectionName}:SignalR:KeepAliveIntervalSeconds must be greater than zero.")
            .Validate(
                o => o.SignalR.ClientTimeoutIntervalSeconds > 0,
                $"{HubOptions.SectionName}:SignalR:ClientTimeoutIntervalSeconds must be greater than zero.")
            .Validate(
                o => o.SignalR.ClientTimeoutIntervalSeconds >= 2L * o.SignalR.KeepAliveIntervalSeconds,
                $"{HubOptions.SectionName}:SignalR:ClientTimeoutIntervalSeconds must be at least twice KeepAliveIntervalSeconds.")
            .ValidateOnStart();

        builder.Services
            .AddControllers()
            .AddJsonOptions(jsonOptions =>
                jsonOptions.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        builder.Services.AddRateLimiter(rateOptions =>
        {
            rateOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            rateOptions.AddPolicy("JoinRateLimit", httpContext =>
            {
                var hubOptions = httpContext.RequestServices.GetRequiredService<IOptions<HubOptions>>().Value;
                var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = hubOptions.JoinRateLimitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            rateOptions.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString();
                }

                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new JoinResponse(
                        Success: false,
                        Role: null,
                        DeviceSessionId: null,
                        HostGameId: null,
                        SessionToken: null,
                        Error: new HubError(HubErrorCode.RateLimited, "Too many join attempts. Please try again later.")),
                    cancellationToken);
            };
        });

        builder.Services.Configure<ForwardedHeadersOptions>(forwardedOptions =>
        {
            forwardedOptions.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            var trustedProxies = builder.Configuration
                .GetSection($"{HubOptions.SectionName}:TrustedProxies")
                .Get<string[]>() ?? [];

            foreach (var proxy in trustedProxies)
            {
                if (proxy.Contains('/'))
                {
                    forwardedOptions.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(proxy));
                }
                else
                {
                    forwardedOptions.KnownProxies.Add(IPAddress.Parse(proxy));
                }
            }
        });

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IRoomCodeGenerator, CryptographicRoomCodeGenerator>();
        builder.Services.AddSingleton<IRoomManager, RoomManager>();
        builder.Services.AddSingleton<IRelayRateLimiter, RelayRateLimiter>();
        builder.Services.AddSingleton<IPeerNotificationScheduler, PeerNotificationScheduler>();
        builder.Services.AddSignalR(signalROptions =>
        {
            var maxPayload = builder.Configuration.GetValue(
                $"{HubOptions.SectionName}:MaxRelayPayloadBytes",
                256 * 1024);
            signalROptions.MaximumReceiveMessageSize = maxPayload + RelayHub.ReceiveMessageSizeOverheadBytes;

            var keepAliveSeconds = builder.Configuration.GetValue(
                $"{HubOptions.SectionName}:SignalR:KeepAliveIntervalSeconds",
                SignalROptions.DefaultKeepAliveIntervalSeconds);
            signalROptions.KeepAliveInterval = TimeSpan.FromSeconds(keepAliveSeconds);

            var clientTimeoutSeconds = builder.Configuration.GetValue(
                $"{HubOptions.SectionName}:SignalR:ClientTimeoutIntervalSeconds",
                SignalROptions.DefaultClientTimeoutIntervalSeconds);
            signalROptions.ClientTimeoutInterval = TimeSpan.FromSeconds(clientTimeoutSeconds);
        });

        configureBuilder?.Invoke(builder);

        var app = builder.Build();

        app.UseForwardedHeaders();
        app.UseRateLimiter();
        app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
        app.UseMiddleware<RelayAuthenticationMiddleware>();
        app.MapControllers();
        app.MapGet("/health", () =>
        {
            var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
            return Results.Ok(new
            {
                status = "healthy",
                service = "Sanet.Transport.SignalR.Hub",
                version
            });
        });

        app.MapHub<RelayHub>(RelayAuthenticationDefaults.HubPath, hubOptions =>
        {
            hubOptions.Transports = HttpTransportType.WebSockets;
        });

        return app;
    }
}
