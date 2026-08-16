using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanet.Transport.SignalR.Hub.Security;
using Sanet.Transport.SignalR.Hub.Tests.TestLoggers;

namespace Sanet.Transport.SignalR.Hub.Tests;

/// <summary>
/// Boots the real relay hub over a Kestrel listener on an ephemeral loopback port so
/// integration tests exercise genuine HTTP and WebSocket transports instead of the
/// in-process TestServer pipe. Public surface mirrors the previous
/// <see cref="WebApplicationFactory{TEntryPoint}"/>-based fixture.
/// </summary>
public sealed class HubApplicationFactory : IAsyncDisposable
{
    public const string ApiKey = "test-api-key";

    private readonly int _maxConcurrentRooms;
    private readonly int _joinRateLimitPerMinute;
    private readonly int _relayRateLimitPerMinute;
    private readonly int _maxRelayPayloadBytes;
    private readonly int _roomTtlSeconds;
    private readonly int _dissolutionGracePeriodSeconds;
    private readonly int _relayTicketTtlSeconds;
    private readonly int _peerDisconnectNotificationDelaySeconds;
    private readonly int _signalRKeepAliveIntervalSeconds;
    private readonly int _signalRClientTimeoutIntervalSeconds;
    private readonly TimeProvider? _timeProvider;
    private readonly CapturingLogger<ApiKeyAuthenticationMiddleware>? _apiKeyAuthenticationLogger;
    private readonly CapturingLogger<RelayAuthenticationMiddleware>? _relayAuthenticationLogger;

    private readonly object _gate = new();
    private WebApplication? _app;
    private string? _baseAddress;
    private bool _started;

    public HubApplicationFactory(
        int maxConcurrentRooms = 10,
        int joinRateLimitPerMinute = 100,
        int relayRateLimitPerMinute = 1000,
        int maxRelayPayloadBytes = 256 * 1024,
        int roomTtlSeconds = 7200,
        int dissolutionGracePeriodSeconds = 30,
        int relayTicketTtlSeconds = 60,
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
        _relayTicketTtlSeconds = relayTicketTtlSeconds;
        _peerDisconnectNotificationDelaySeconds = peerDisconnectNotificationDelaySeconds;
        _signalRKeepAliveIntervalSeconds = signalRKeepAliveIntervalSeconds;
        _signalRClientTimeoutIntervalSeconds = signalRClientTimeoutIntervalSeconds;
        _timeProvider = timeProvider;
        _apiKeyAuthenticationLogger = apiKeyAuthenticationLogger;
        _relayAuthenticationLogger = relayAuthenticationLogger;
    }

    public IServiceProvider Services => App.Services;

    public HttpClient CreateClient()
    {
        _ = App;
        return new HttpClient { BaseAddress = new Uri(_baseAddress!) };
    }

    public HubConnection CreateRelayHubConnection(string? relayTicket)
    {
        _ = App;
        var url = BuildRelayHubUrl(_baseAddress!, relayTicket);

        return new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.Transports = HttpTransportType.WebSockets;
            })
            .WithKeepAliveInterval(TimeSpan.FromDays(1))
            .WithServerTimeout(TimeSpan.FromDays(2))
            .Build();
    }

    public static string BuildRelayHubUrl(string baseAddress, string? relayTicket)
    {
        var builder = new UriBuilder(new Uri(new Uri(baseAddress), RelayAuthenticationDefaults.HubPath));
        var queryParts = new List<string>();

        if (relayTicket is not null)
        {
            queryParts.Add(
                $"{ApiKeyAuthenticationDefaults.TicketQueryParameterName}={Uri.EscapeDataString(relayTicket)}");
        }

        builder.Query = string.Join('&', queryParts);
        return builder.Uri.AbsoluteUri;
    }

    public async ValueTask DisposeAsync()
    {
        WebApplication? app;
        lock (_gate)
        {
            app = _app;
            _app = null;
        }

        if (app is null)
        {
            return;
        }

        if (_started)
        {
            await app.StopAsync();
        }

        await app.DisposeAsync();
    }

    private WebApplication App
    {
        get
        {
            lock (_gate)
            {
                if (_app is null)
                {
                    var app = HubAppBuilder.CreateApp(
                        [],
                        options: new WebApplicationOptions
                        {
                            Args = [],
                            EnvironmentName = "Testing",
                            ApplicationName = typeof(HubAppBuilder).Assembly.GetName().Name
                        },
                        configureBuilder: ConfigureTestBuilder);

                    app.Urls.Add("http://127.0.0.1:0");
                    app.StartAsync().GetAwaiter().GetResult();

                    _app = app;
                    _started = true;
                    _baseAddress = ResolveBaseAddress(app);
                }

                return _app;
            }
        }
    }

    private void ConfigureTestBuilder(WebApplicationBuilder builder)
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Hub:ApiKey"] = ApiKey,
            ["Hub:MaxConcurrentRooms"] = _maxConcurrentRooms.ToString(),
            ["Hub:JoinRateLimitPerMinute"] = _joinRateLimitPerMinute.ToString(),
            ["Hub:RelayRateLimitPerMinute"] = _relayRateLimitPerMinute.ToString(),
            ["Hub:MaxRelayPayloadBytes"] = _maxRelayPayloadBytes.ToString(),
            ["Hub:RoomTtlSeconds"] = _roomTtlSeconds.ToString(),
            ["Hub:DissolutionGracePeriodSeconds"] = _dissolutionGracePeriodSeconds.ToString(),
            ["Hub:RelayTicketTtlSeconds"] = _relayTicketTtlSeconds.ToString(),
            ["Hub:PeerDisconnectNotificationDelaySeconds"] = _peerDisconnectNotificationDelaySeconds.ToString(),
            ["Hub:SignalR:KeepAliveIntervalSeconds"] = _signalRKeepAliveIntervalSeconds.ToString(),
            ["Hub:SignalR:ClientTimeoutIntervalSeconds"] = _signalRClientTimeoutIntervalSeconds.ToString()
        });

        builder.Services.PostConfigure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
        {
            options.KeepAliveInterval = TimeSpan.FromSeconds(_signalRKeepAliveIntervalSeconds);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(_signalRClientTimeoutIntervalSeconds);
        });

        if (_timeProvider is not null)
        {
            var existing = builder.Services.Where(descriptor => descriptor.ServiceType == typeof(TimeProvider)).ToList();
            foreach (var descriptor in existing)
            {
                builder.Services.Remove(descriptor);
            }

            builder.Services.AddSingleton(_timeProvider);
        }

        if (_apiKeyAuthenticationLogger is not null)
        {
            builder.Services.AddSingleton<ILogger<ApiKeyAuthenticationMiddleware>>(_apiKeyAuthenticationLogger);
        }

        if (_relayAuthenticationLogger is not null)
        {
            builder.Services.AddSingleton<ILogger<RelayAuthenticationMiddleware>>(_relayAuthenticationLogger);
        }
    }

    private static string ResolveBaseAddress(WebApplication app)
    {
        var address = app.Urls.FirstOrDefault();
        if (string.IsNullOrEmpty(address))
        {
            throw new InvalidOperationException("The hub application did not expose a bound address.");
        }

        return address;
    }
}
