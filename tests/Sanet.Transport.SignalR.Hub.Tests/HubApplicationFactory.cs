using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanet.Transport.SignalR.Hub.Security;
using Sanet.Transport.SignalR.Hub.Tests.TestLoggers;

namespace Sanet.Transport.SignalR.Hub.Tests;

public sealed class HubApplicationFactory : WebApplicationFactory<global::Program>
{
    public const string ApiKey = "test-api-key";

    private readonly int _maxConcurrentRooms;
    private readonly int _joinRateLimitPerMinute;
    private readonly int _relayRateLimitPerMinute;
    private readonly int _maxRelayPayloadBytes;
    private readonly int _roomTtlSeconds;
    private readonly int _dissolutionGracePeriodSeconds;
    private readonly int _peerDisconnectNotificationDelaySeconds;
    private readonly int _signalRKeepAliveIntervalSeconds;
    private readonly int _signalRClientTimeoutIntervalSeconds;
    private readonly TimeProvider? _timeProvider;
    private readonly CapturingLogger<ApiKeyAuthenticationMiddleware>? _apiKeyAuthenticationLogger;
    private readonly CapturingLogger<RelayAuthenticationMiddleware>? _relayAuthenticationLogger;

    public HubApplicationFactory(
        int maxConcurrentRooms = 10,
        int joinRateLimitPerMinute = 100,
        int relayRateLimitPerMinute = 1000,
        int maxRelayPayloadBytes = 256 * 1024,
        int roomTtlSeconds = 7200,
        int dissolutionGracePeriodSeconds = 30,
        int peerDisconnectNotificationDelaySeconds = 5,
        int signalRKeepAliveIntervalSeconds = 86400,
        int signalRClientTimeoutIntervalSeconds = 172800,
        TimeProvider? timeProvider = null,
        CapturingLogger<ApiKeyAuthenticationMiddleware>? apiKeyAuthenticationLogger = null,
        CapturingLogger<RelayAuthenticationMiddleware>? relayAuthenticationLogger = null)
    {
        _maxConcurrentRooms = maxConcurrentRooms;
        _joinRateLimitPerMinute = joinRateLimitPerMinute;
        _relayRateLimitPerMinute = relayRateLimitPerMinute;
        _maxRelayPayloadBytes = maxRelayPayloadBytes;
        _roomTtlSeconds = roomTtlSeconds;
        _dissolutionGracePeriodSeconds = dissolutionGracePeriodSeconds;
        _peerDisconnectNotificationDelaySeconds = peerDisconnectNotificationDelaySeconds;
        _signalRKeepAliveIntervalSeconds = signalRKeepAliveIntervalSeconds;
        _signalRClientTimeoutIntervalSeconds = signalRClientTimeoutIntervalSeconds;
        _timeProvider = timeProvider;
        _apiKeyAuthenticationLogger = apiKeyAuthenticationLogger;
        _relayAuthenticationLogger = relayAuthenticationLogger;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hub:ApiKey"] = ApiKey,
                ["Hub:MaxConcurrentRooms"] = _maxConcurrentRooms.ToString(),
                ["Hub:JoinRateLimitPerMinute"] = _joinRateLimitPerMinute.ToString(),
                ["Hub:RelayRateLimitPerMinute"] = _relayRateLimitPerMinute.ToString(),
                ["Hub:MaxRelayPayloadBytes"] = _maxRelayPayloadBytes.ToString(),
                ["Hub:RoomTtlSeconds"] = _roomTtlSeconds.ToString(),
                ["Hub:DissolutionGracePeriodSeconds"] = _dissolutionGracePeriodSeconds.ToString(),
                ["Hub:PeerDisconnectNotificationDelaySeconds"] = _peerDisconnectNotificationDelaySeconds.ToString(),
                ["Hub:SignalR:KeepAliveIntervalSeconds"] = _signalRKeepAliveIntervalSeconds.ToString(),
                ["Hub:SignalR:ClientTimeoutIntervalSeconds"] = _signalRClientTimeoutIntervalSeconds.ToString()
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
            {
                options.KeepAliveInterval = TimeSpan.FromSeconds(_signalRKeepAliveIntervalSeconds);
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(_signalRClientTimeoutIntervalSeconds);
            });

            if (_timeProvider is not null)
            {
                var existing = services.Where(descriptor => descriptor.ServiceType == typeof(TimeProvider)).ToList();
                foreach (var descriptor in existing)
                {
                    services.Remove(descriptor);
                }

                services.AddSingleton(_timeProvider);
            }

            if (_apiKeyAuthenticationLogger is not null)
            {
                services.AddSingleton<ILogger<ApiKeyAuthenticationMiddleware>>(_apiKeyAuthenticationLogger);
            }

            if (_relayAuthenticationLogger is not null)
            {
                services.AddSingleton<ILogger<RelayAuthenticationMiddleware>>(_relayAuthenticationLogger);
            }
        });
    }

    public HubConnection CreateRelayHubConnection(string? apiKey, string? sessionToken)
    {
        var url = BuildRelayHubUrl(Server.BaseAddress.ToString(), apiKey, sessionToken);

        return new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var webSocketClient = Server.CreateWebSocketClient();
                    return await webSocketClient.ConnectAsync(context.Uri, cancellationToken);
                };
            })
            .WithKeepAliveInterval(TimeSpan.FromDays(1))
            .WithServerTimeout(TimeSpan.FromDays(2))
            .Build();
    }

    public static string BuildRelayHubUrl(string baseAddress, string? apiKey, string? sessionToken)
    {
        var builder = new UriBuilder(new Uri(new Uri(baseAddress), RelayAuthenticationDefaults.HubPath));
        var queryParts = new List<string>();

        if (apiKey is not null)
        {
            queryParts.Add(
                $"{ApiKeyAuthenticationDefaults.ApiKeyQueryParameterName}={Uri.EscapeDataString(apiKey)}");
        }

        if (sessionToken is not null)
        {
            queryParts.Add(
                $"{ApiKeyAuthenticationDefaults.SessionTokenQueryParameterName}={Uri.EscapeDataString(sessionToken)}");
        }

        builder.Query = string.Join('&', queryParts);
        return builder.Uri.AbsoluteUri;
    }
}
