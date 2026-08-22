using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sanet.Transport.SignalR.Hub.Configuration;
using Sanet.Transport.Relay.Contracts;
using Sanet.Transport.SignalR.Hub.Security;
using Sanet.Transport.SignalR.Hub.Tests.TestLoggers;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests;

public class HubConfigurationTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    
    [Fact]
    public async Task RoomTtlAndGracePeriod_BindFromConfiguration()
    {
        var now = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var fakeTimeProvider = new FakeTimeProvider(now);
        await using var factory = new HubApplicationFactory(
            roomTtlSeconds: 3600,
            dissolutionGracePeriodSeconds: 60,
            timeProvider: fakeTimeProvider);
        using var client = factory.CreateClient();

        using var createResponse = await CreateRoomAsync(client, Guid.NewGuid(), HubApplicationFactory.ApiKey);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var result = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(JsonOptions);
        result.ShouldNotBeNull();
        result.ExpiresAt.ShouldNotBeNull();
        result.ExpiresAt!.Value.ShouldBe(now.AddSeconds(3600));

        // Verify dissolution grace period is also bound from configuration.
        var hubOptions = factory.Services.GetRequiredService<IOptions<HubOptions>>().Value;
        hubOptions.DissolutionGracePeriodSeconds.ShouldBe(60);
    }

    [Fact]
    public void DefaultOptions_HaveExpectedValues()
    {
        var options = new HubOptions();

        options.RoomTtlSeconds.ShouldBe(7200);
        options.DissolutionGracePeriodSeconds.ShouldBe(30);
        options.RelayTicketTtlSeconds.ShouldBe(60);
        options.MaxConcurrentRooms.ShouldBe(100);
        options.JoinRateLimitPerMinute.ShouldBe(10);
        options.RelayRateLimitPerMinute.ShouldBe(120);
        options.MaxRelayPayloadBytes.ShouldBe(256 * 1024);
        options.PeerDisconnectNotificationDelaySeconds.ShouldBe(5);
        options.SignalR.KeepAliveIntervalSeconds.ShouldBe(15);
        options.SignalR.ClientTimeoutIntervalSeconds.ShouldBe(30);
    }

    [Fact]
    public async Task SignalROptions_BindFromConfiguration()
    {
        await using var factory = new HubApplicationFactory(
            signalRKeepAliveIntervalSeconds: 10,
            signalRClientTimeoutIntervalSeconds: 40);
        using var client = factory.CreateClient();

        var options = factory.Services.GetRequiredService<IOptions<HubOptions>>().Value;
        options.SignalR.KeepAliveIntervalSeconds.ShouldBe(10);
        options.SignalR.ClientTimeoutIntervalSeconds.ShouldBe(40);
    }

    [Fact]
    public async Task TrustedProxies_PlainIp_IsAddedToKnownProxies()
    {
        await using var factory = new HubApplicationFactory(trustedProxies: ["203.0.113.5"]);
        using var client = factory.CreateClient();

        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        options.KnownProxies.Select(ip => ip.ToString()).ShouldContain("203.0.113.5");
    }

    [Fact]
    public async Task TrustedProxies_Cidr_IsAddedToKnownIpNetworks()
    {
        await using var factory = new HubApplicationFactory(trustedProxies: ["10.0.0.0/8"]);
        using var client = factory.CreateClient();

        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        options.KnownIPNetworks.Select(network => network.ToString()).ShouldContain("10.0.0.0/8");
    }

    [Fact]
    public void InvalidPeerDisconnectNotificationDelaySeconds_Negative_FailsStartupValidation()
    {
        var factory = new HubApplicationFactory(peerDisconnectNotificationDelaySeconds: -1);

        var ex = Should.Throw<OptionsValidationException>(() =>
        {
            factory.CreateClient();
        });

        ex.Message.ShouldContain("PeerDisconnectNotificationDelaySeconds");
    }

    [Fact]
    public async Task ZeroPeerDisconnectNotificationDelay_IsAcceptedByValidation()
    {
        await using var factory = new HubApplicationFactory(peerDisconnectNotificationDelaySeconds: 0);
        using var client = factory.CreateClient();

        var options = factory.Services.GetRequiredService<IOptions<HubOptions>>().Value;
        options.PeerDisconnectNotificationDelaySeconds.ShouldBe(0);
    }

    [Fact]
    public void InvalidKeepAliveInterval_Zero_FailsStartupValidation()
    {
        var factory = new HubApplicationFactory(signalRKeepAliveIntervalSeconds: 0);

        var ex = Should.Throw<OptionsValidationException>(() =>
        {
            factory.CreateClient();
        });

        ex.Message.ShouldContain("KeepAliveIntervalSeconds");
    }

    [Fact]
    public void InvalidClientTimeoutInterval_Negative_FailsStartupValidation()
    {
        var factory = new HubApplicationFactory(signalRClientTimeoutIntervalSeconds: -1);

        var ex = Should.Throw<OptionsValidationException>(() =>
        {
            factory.CreateClient();
        });

        ex.Message.ShouldContain("ClientTimeoutIntervalSeconds");
    }

    [Fact]
    public void InvalidClientTimeoutInterval_LessThanTwiceKeepAlive_FailsStartupValidation()
    {
        var factory = new HubApplicationFactory(
            signalRKeepAliveIntervalSeconds: 15,
            signalRClientTimeoutIntervalSeconds: 20);

        var ex = Should.Throw<OptionsValidationException>(() =>
        {
            factory.CreateClient();
        });

        ex.Message.ShouldContain("at least twice");
    }

    [Fact]
    public void InvalidRoomTtlSeconds_Zero_FailsStartupValidation()
    {
        var factory = new HubApplicationFactory(roomTtlSeconds: 0);

        var ex = Should.Throw<OptionsValidationException>(() =>
        {
            factory.CreateClient();
        });

        ex.Message.ShouldContain("RoomTtlSeconds");
    }

    [Fact]
    public void InvalidRelayTicketTtlSeconds_Zero_FailsStartupValidation()
    {
        var factory = new HubApplicationFactory(relayTicketTtlSeconds: 0);

        var ex = Should.Throw<OptionsValidationException>(() =>
        {
            factory.CreateClient();
        });

        ex.Message.ShouldContain("RelayTicketTtlSeconds");
    }

    [Fact]
    public void InvalidRelayTicketTtlSeconds_Negative_FailsStartupValidation()
    {
        var factory = new HubApplicationFactory(relayTicketTtlSeconds: -5);

        var ex = Should.Throw<OptionsValidationException>(() =>
        {
            factory.CreateClient();
        });

        ex.Message.ShouldContain("RelayTicketTtlSeconds");
    }

    [Fact]
    public void InvalidDissolutionGracePeriodSeconds_Zero_FailsStartupValidation()
    {
        var factory = new HubApplicationFactory(dissolutionGracePeriodSeconds: 0);

        var ex = Should.Throw<OptionsValidationException>(() =>
        {
            factory.CreateClient();
        });

        ex.Message.ShouldContain("DissolutionGracePeriodSeconds");
    }

    [Fact]
    public void InvalidRoomTtlSeconds_Negative_FailsStartupValidation()
    {
        var factory = new HubApplicationFactory(roomTtlSeconds: -1);

        var ex = Should.Throw<OptionsValidationException>(() =>
        {
            factory.CreateClient();
        });

        ex.Message.ShouldContain("RoomTtlSeconds");
    }

    [Fact]
    public Task InvalidDissolutionGracePeriodSeconds_Negative_FailsStartupValidation()
    {
        try
        {
            var factory = new HubApplicationFactory(dissolutionGracePeriodSeconds: -5);

            var ex = Should.Throw<OptionsValidationException>(() =>
            {
                factory.CreateClient();
            });

            ex.Message.ShouldContain("DissolutionGracePeriodSeconds");
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    [Fact]
    public async Task GetHealth_ReturnsHealthyStatusWithoutRequiringApiKey()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"status\"");
        body.ShouldContain("\"service\"");
        body.ShouldContain("\"version\"");
        body.ShouldNotContain(HubApplicationFactory.ApiKey);
    }

    [Fact]
    public async Task GetHealth_DoesNotExposeSecrets()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("ApiKey");
        body.ShouldNotContain("SessionToken");
        body.ShouldNotContain("sessionToken");
        body.ShouldNotContain(HubApplicationFactory.ApiKey);
    }
    
    [Theory]
    [InlineData(null)]
    [InlineData("not-the-configured-key")]
    public async Task UnauthenticatedApiRequest_DoesNotEchoApiKey(string? apiKey)
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rooms");
        request.Content = JsonContent.Create(new CreateRoomRequest(Guid.NewGuid()));
        if (apiKey is not null)
        {
            request.Headers.Add(ApiKeyAuthenticationDefaults.HeaderName, apiKey);
        }

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain(HubApplicationFactory.ApiKey);
        body.ShouldNotContain("ApiKey");
        body.ShouldNotContain("X-Api-Key");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UnauthenticatedApiRequest_DoesNotEchoSessionToken(string? sessionToken)
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rooms/ABC234/lock");
        if (sessionToken is not null)
        {
            request.Headers.Add("Session-Token", sessionToken);
        }
        request.Headers.Add(ApiKeyAuthenticationDefaults.HeaderName, HubApplicationFactory.ApiKey);

        using var response = await client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("sessionToken");
        body.ShouldNotContain(HubApplicationFactory.ApiKey);
    }

    [Fact]
    public async Task NonApiPath_PassesThroughWithoutAuthentication()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");

        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiKeyAuthenticationMiddleware_DoesNotLogApiKey()
    {
        var logger = new CapturingLogger<ApiKeyAuthenticationMiddleware>();
        await using var factory = new HubApplicationFactory(apiKeyAuthenticationLogger: logger);
        using var client = factory.CreateClient();

        const string suppliedApiKey = "distinctive-wrong-api-key";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rooms");
        request.Content = JsonContent.Create(new CreateRoomRequest(Guid.NewGuid()));
        request.Headers.Add(ApiKeyAuthenticationDefaults.HeaderName, suppliedApiKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain(HubApplicationFactory.ApiKey);
        body.ShouldNotContain(suppliedApiKey);

        logger.GetMessages(LogLevel.Warning).ShouldContain(
            message => message.Contains("API key missing or invalid", StringComparison.Ordinal));
        logger.GetMessages(LogLevel.Warning).ShouldNotContain(
            message => message.Contains(suppliedApiKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RelayAuthenticationMiddleware_DoesNotLogRelayTicket()
    {
        var logger = new CapturingLogger<RelayAuthenticationMiddleware>();
        await using var factory = new HubApplicationFactory(relayAuthenticationLogger: logger);
        using var client = factory.CreateClient();

        const string suppliedTicket = "distinctive-invalid-relay-ticket";
        var url = HubApplicationFactory.BuildRelayHubUrl(
            client.BaseAddress!.ToString(),
            suppliedTicket);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain(HubApplicationFactory.ApiKey);
        body.ShouldNotContain(suppliedTicket);

        logger.GetMessages(LogLevel.Warning).ShouldContain(
            message => message.Contains("relay ticket not recognized", StringComparison.Ordinal));
        logger.GetMessages(LogLevel.Warning).ShouldNotContain(
            message => message.Contains(suppliedTicket, StringComparison.Ordinal));
    }
    
    private static async Task<HttpResponseMessage> CreateRoomAsync(
        HttpClient client,
        Guid gameId,
        string? apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rooms");
        request.Content = JsonContent.Create(new CreateRoomRequest(gameId));

        if (apiKey is not null)
        {
            request.Headers.Add(ApiKeyAuthenticationDefaults.HeaderName, apiKey);
        }

        return await client.SendAsync(request);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
