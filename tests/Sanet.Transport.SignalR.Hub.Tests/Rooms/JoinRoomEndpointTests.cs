using System.Net;
using System.Net.Http.Json;
using Sanet.Transport.SignalR.Hub.Contracts;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests.Rooms;

public class JoinRoomEndpointTests
{
    [Fact]
    public async Task JoinRoom_ReadyRoom_ReturnsDeviceSessionAndHostGameId()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();
        var hostGameId = Guid.NewGuid();

        using var createResponse = await RoomApiClient.CreateRoomAsync(client, hostGameId);
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);
        var roomCode = createResult!.RoomCode!;

        await RoomApiClient.MarkReadyAsync(client, roomCode, createResult.SessionToken!);

        using var joinResponse = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken: null);

        joinResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await joinResponse.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Role.ShouldBe("Client");
        result.DeviceSessionId.ShouldNotBeNull();
        result.DeviceSessionId.ShouldNotBe(Guid.Empty);
        result.HostGameId.ShouldBe(hostGameId);
        string.IsNullOrWhiteSpace(result.SessionToken).ShouldBeFalse();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public async Task JoinRoom_MissingRoom_ReturnsNotFound()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.JoinRoomAsync(client, "NOEXIST", sessionToken: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var result = await response.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error!.Code.ShouldBe(HubErrorCode.RoomNotFound);
    }

    [Fact]
    public async Task JoinRoom_NotReadyRoom_ReturnsConflict()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var createResponse = await RoomApiClient.CreateRoomAsync(client, Guid.NewGuid());
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);
        var roomCode = createResult!.RoomCode!;

        using var joinResponse = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken: null);

        joinResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var result = await joinResponse.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error!.Code.ShouldBe(HubErrorCode.HostNotReady);
    }

    [Fact]
    public async Task JoinRoom_RejoinWithValidSessionToken_ReusesDeviceSession()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();
        var hostGameId = Guid.NewGuid();

        using var createResponse = await RoomApiClient.CreateRoomAsync(client, hostGameId);
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);
        var roomCode = createResult!.RoomCode!;

        await RoomApiClient.MarkReadyAsync(client, roomCode, createResult.SessionToken!);

        using var firstJoin = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken: null);
        var first = await firstJoin.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        first!.DeviceSessionId.ShouldNotBeNull();

        using var rejoin = await RoomApiClient.JoinRoomAsync(client, roomCode, first.SessionToken);

        rejoin.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await rejoin.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.DeviceSessionId.ShouldBe(first.DeviceSessionId);
        result.HostGameId.ShouldBe(hostGameId);
    }

    [Fact]
    public async Task JoinRoom_TwoNewDevices_EachGetDistinctDeviceSessions()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();
        var hostGameId = Guid.NewGuid();

        using var createResponse = await RoomApiClient.CreateRoomAsync(client, hostGameId);
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);
        var roomCode = createResult!.RoomCode!;

        await RoomApiClient.MarkReadyAsync(client, roomCode, createResult.SessionToken!);

        using var join1 = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken: null);
        using var join2 = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken: null);

        join1.StatusCode.ShouldBe(HttpStatusCode.OK);
        join2.StatusCode.ShouldBe(HttpStatusCode.OK);

        var first = await join1.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        var second = await join2.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        first!.DeviceSessionId.ShouldNotBe(second!.DeviceSessionId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-configured-key")]
    public async Task JoinRoom_WithMissingOrInvalidApiKey_IsRejectedWithoutLeakingConfiguredKey(string? apiKey)
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.JoinRoomAsync(client, "ABC234", sessionToken: null, apiKey);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain(HubApplicationFactory.ApiKey);
    }

    [Fact]
    public async Task JoinRoom_ExceedsRateLimit_Returns429()
    {
        await using var factory = new HubApplicationFactory(joinRateLimitPerMinute: 2);
        using var client = factory.CreateClient();

        var hostGameId = Guid.NewGuid();
        using var createResponse = await RoomApiClient.CreateRoomAsync(client, hostGameId);
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);
        var roomCode = createResult!.RoomCode!;
        await RoomApiClient.MarkReadyAsync(client, roomCode, createResult.SessionToken!);

        using var r1 = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken: null);
        using var r2 = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken: null);
        using var r3 = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken: null);

        r1.StatusCode.ShouldBe(HttpStatusCode.OK);
        r2.StatusCode.ShouldBe(HttpStatusCode.OK);
        r3.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task MarkRoomReady_WithHostSession_ReturnsOk()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();
        var hostGameId = Guid.NewGuid();

        using var createResponse = await RoomApiClient.CreateRoomAsync(client, hostGameId);
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);
        var roomCode = createResult!.RoomCode!;

        using var response = await RoomApiClient.MarkReadyAsync(client, roomCode, createResult.SessionToken!);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ReadyResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public async Task MarkRoomReady_NonHost_ReturnsConflict()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var createResponse = await RoomApiClient.CreateRoomAsync(client, Guid.NewGuid());
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);
        var roomCode = createResult!.RoomCode!;

        using var response = await RoomApiClient.MarkReadyAsync(client, roomCode, "not-the-host-token");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var result = await response.Content.ReadFromJsonAsync<ReadyResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error!.Code.ShouldBe(HubErrorCode.NotHost);
    }

    [Fact]
    public async Task MarkRoomReady_MissingRoom_ReturnsNotFound()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.MarkReadyAsync(client, "NOEXIST", "any-token");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var result = await response.Content.ReadFromJsonAsync<ReadyResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error!.Code.ShouldBe(HubErrorCode.RoomNotFound);
    }
}
