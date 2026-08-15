using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Sanet.Transport.SignalR.Hub.Contracts;
using Sanet.Transport.SignalR.Hub.Relay;
using Sanet.Transport.SignalR.Hub.Security;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests.Relay;

public class RelayHubConnectionTests
{
    private const string GroupProbeMethod = "__relay_group_probe";

    [Fact]
    public async Task Connect_WithValidCredentials_AttachesToBoundRoomGroupOnly()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        var host = await CreateReadyHostAsync(client);
        var other = await CreateReadyHostAsync(client);

        await using var hostConnection = factory.CreateRelayHubConnection(host.SessionToken);
        await using var otherConnection = factory.CreateRelayHubConnection(other.SessionToken);

        var hostProbe = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherProbe = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        hostConnection.On<string>(GroupProbeMethod, payload => hostProbe.TrySetResult(payload));
        otherConnection.On<string>(GroupProbeMethod, payload => otherProbe.TrySetResult(payload));

        await hostConnection.StartAsync();
        await otherConnection.StartAsync();

        hostConnection.State.ShouldBe(HubConnectionState.Connected);
        otherConnection.State.ShouldBe(HubConnectionState.Connected);

        var hubContext = factory.Services.GetRequiredService<IHubContext<RelayHub>>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!hostProbe.Task.IsCompleted && DateTime.UtcNow < deadline)
        {
            await hubContext.Clients.Group(host.RoomCode).SendAsync(GroupProbeMethod, "bound-room");
            await Task.WhenAny(hostProbe.Task, Task.Delay(500));
        }

        var received = await hostProbe.Task.WaitAsync(TimeSpan.FromSeconds(30));
        received.ShouldBe("bound-room");

        var otherCompleted = await Task.WhenAny(otherProbe.Task, Task.Delay(1000));
        otherCompleted.ShouldNotBe(otherProbe.Task);
    }

    [Fact]
    public async Task Connect_WithoutApiKey_IsAcceptedOnSessionTokenOnly()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();
        var host = await CreateReadyHostAsync(client);

        await using var connection = factory.CreateRelayHubConnection(host.SessionToken);

        await connection.StartAsync();

        connection.State.ShouldBe(HubConnectionState.Connected);
    }

    [Fact]
    public async Task Connect_WithApiKeyOnly_IsRejected()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        var baseUri = new Uri(client.BaseAddress!.ToString());
        var negotiateUri = new UriBuilder(new Uri(baseUri, RelayAuthenticationDefaults.HubPath))
        {
            Query = "negotiateVersion=1"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, negotiateUri.Uri);
        request.Headers.Add(ApiKeyAuthenticationDefaults.HeaderName, HubApplicationFactory.ApiKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-valid-session-token")]
    public async Task Connect_WithMissingOrMalformedSessionToken_IsRejectedWithoutLeakingCredentials(
        string? sessionToken)
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await PostNegotiateAsync(client, sessionToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBeEmpty();
        body.ShouldNotContain(HubApplicationFactory.ApiKey);
        if (!string.IsNullOrEmpty(sessionToken))
        {
            body.ShouldNotContain(sessionToken);
        }
    }

    [Fact]
    public async Task Connect_WithExpiredSessionToken_IsRejectedWithoutLeakingToken()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
        await using var factory = new HubApplicationFactory(timeProvider: timeProvider);
        using var client = factory.CreateClient();
        var host = await CreateReadyHostAsync(client);

        timeProvider.Advance(TimeSpan.FromHours(2).Add(TimeSpan.FromMinutes(1)));

        using var response = await PostNegotiateAsync(client, host.SessionToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBeEmpty();
        body.ShouldNotContain(HubApplicationFactory.ApiKey);
        body.ShouldNotContain(host.SessionToken);
    }

    [Fact]
    public async Task Connect_WithRevokedSessionToken_IsRejectedWithoutLeakingToken()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        var host = await CreateReadyHostAsync(client);
        var join = await JoinRoomAsync(client, host.RoomCode, sessionToken: null);
        join.SessionToken.ShouldNotBeNull();

        using var removeResponse = await RoomApiClient.RemoveMemberAsync(
            client, host.RoomCode, join.DeviceSessionId!.Value, host.SessionToken);
        removeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var response = await PostNegotiateAsync(client, join.SessionToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBeEmpty();
        body.ShouldNotContain(HubApplicationFactory.ApiKey);
        body.ShouldNotContain(join.SessionToken!);
    }

    [Fact]
    public async Task Connect_WithValidClosedRoomSessionToken_IsAccepted()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        var host = await CreateReadyHostAsync(client);

        using var closeResponse = await RoomApiClient.CloseRoomAsync(client, host.RoomCode, host.SessionToken);
        closeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var connection = factory.CreateRelayHubConnection(host.SessionToken);

        await connection.StartAsync();

        connection.State.ShouldBe(HubConnectionState.Connected);
    }

    [Fact]
    public async Task Invoke_RoomLifecycleMethods_FailBecauseHubExposesNoManagementRpcs()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();
        var host = await CreateReadyHostAsync(client);

        await using var connection = factory.CreateRelayHubConnection(host.SessionToken);
        await connection.StartAsync();

        foreach (var methodName in new[] { "CreateRoom", "JoinRoom", "MarkReady", "CloseRoom", "RemoveMember" })
        {
            var exception = await Should.ThrowAsync<HubException>(
                async () => await connection.InvokeAsync(methodName));
            exception.Message.ShouldNotContain(HubApplicationFactory.ApiKey);
            exception.Message.ShouldNotContain(host.SessionToken);
        }
    }

    private static async Task<ReadyHost> CreateReadyHostAsync(HttpClient client)
    {
        using var createResponse = await RoomApiClient.CreateRoomAsync(client, Guid.NewGuid());
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);
        created.ShouldNotBeNull();
        created.Success.ShouldBeTrue();
        created.RoomCode.ShouldNotBeNull();
        created.SessionToken.ShouldNotBeNull();

        using var readyResponse = await RoomApiClient.MarkReadyAsync(client, created.RoomCode, created.SessionToken);
        readyResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        return new ReadyHost(created.RoomCode, created.SessionToken);
    }

    private static async Task<JoinResponse> JoinRoomAsync(
        HttpClient client,
        string roomCode,
        string? sessionToken)
    {
        using var response = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        return result;
    }

    private static async Task<HttpResponseMessage> PostNegotiateAsync(
        HttpClient client,
        string? sessionToken)
    {
        var baseUri = new Uri(client.BaseAddress!.ToString());
        var negotiateBuilder = new UriBuilder(new Uri(baseUri, RelayAuthenticationDefaults.HubPath));
        var queryParts = new List<string>();

        if (sessionToken is not null)
        {
            queryParts.Add(
                $"{ApiKeyAuthenticationDefaults.SessionTokenQueryParameterName}={Uri.EscapeDataString(sessionToken)}");
        }

        queryParts.Add("negotiateVersion=1");
        negotiateBuilder.Query = string.Join('&', queryParts);

        using var request = new HttpRequestMessage(HttpMethod.Post, negotiateBuilder.Uri);
        return await client.SendAsync(request);
    }

    private sealed record ReadyHost(string RoomCode, string SessionToken);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan offset) => _now += offset;
    }
}
