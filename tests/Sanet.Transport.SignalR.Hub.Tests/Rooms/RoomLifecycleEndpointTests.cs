using System.Net;
using System.Net.Http.Json;
using Sanet.Transport.SignalR.Hub.Contracts;
using Sanet.Transport.SignalR.Hub.Security;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests.Rooms;

public class RoomLifecycleEndpointTests
{
    [Fact]
    public async Task CloseRoom_ActiveRoom_ReturnsOkAndRejectsUnknownJoiners()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        var (roomCode, hostToken) = await CreateReadyRoomAsync(client);

        using var closeResponse = await RoomApiClient.CloseRoomAsync(client, roomCode, hostToken);

        closeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var closeResult = await closeResponse.Content.ReadFromJsonAsync<CloseResponse>(RoomApiClient.JsonOptions);
        closeResult.ShouldNotBeNull();
        closeResult.Success.ShouldBeTrue();
        closeResult.Error.ShouldBeNull();

        using var joinResponse = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken: null);
        joinResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var joinResult = await joinResponse.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        joinResult.ShouldNotBeNull();
        joinResult.Success.ShouldBeFalse();
        joinResult.Error.ShouldNotBeNull();
        joinResult.Error!.Code.ShouldBe(HubErrorCode.RoomFull);
    }

    [Fact]
    public async Task CloseRoom_CreatedRoom_ReturnsConflictInvalidRoomState()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var createResponse = await RoomApiClient.CreateRoomAsync(client, Guid.NewGuid());
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);
        var roomCode = createResult!.RoomCode!;

        using var closeResponse = await RoomApiClient.CloseRoomAsync(
            client,
            roomCode,
            createResult.SessionToken!);

        closeResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var result = await closeResponse.Content.ReadFromJsonAsync<CloseResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error!.Code.ShouldBe(HubErrorCode.InvalidRoomState);

        using var joinResponse = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken: null);
        joinResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var joinResult = await joinResponse.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        joinResult!.Error!.Code.ShouldBe(HubErrorCode.HostNotReady);
    }

    [Fact]
    public async Task CloseRoom_NotFound_ReturnsNotFound()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.CloseRoomAsync(client, "NOEXIST", "any-token");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var result = await response.Content.ReadFromJsonAsync<CloseResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error!.Code.ShouldBe(HubErrorCode.RoomNotFound);
    }

    [Fact]
    public async Task CloseRoom_NonHost_ReturnsConflictAndLeavesRoomActive()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        var (roomCode, _) = await CreateReadyRoomAsync(client);

        using var closeResponse = await RoomApiClient.CloseRoomAsync(client, roomCode, "not-the-host-token");

        closeResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var closeResult = await closeResponse.Content.ReadFromJsonAsync<CloseResponse>(RoomApiClient.JsonOptions);
        closeResult!.Error!.Code.ShouldBe(HubErrorCode.NotHost);

        using var joinResponse = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken: null);
        joinResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task JoinRoom_ClosedRoom_ExistingDeviceSession_Succeeds()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        var (roomCode, hostToken) = await CreateReadyRoomAsync(client);

        using var firstJoin = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken: null);
        firstJoin.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstResult = await firstJoin.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        firstResult.ShouldNotBeNull();

        using var closeResponse = await RoomApiClient.CloseRoomAsync(client, roomCode, hostToken);
        closeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var rejoin = await RoomApiClient.JoinRoomAsync(client, roomCode, firstResult.SessionToken);
        rejoin.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await rejoin.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.DeviceSessionId.ShouldBe(firstResult.DeviceSessionId);
        string.IsNullOrWhiteSpace(result.SessionToken).ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveMember_WithHostAuthorization_ReturnsOk()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        var (roomCode, hostToken) = await CreateReadyRoomAsync(client);

        using var joinResponse = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken: null);
        joinResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var joinResult = await joinResponse.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        var deviceSessionId = joinResult!.DeviceSessionId!.Value;

        using var removeResponse = await RoomApiClient.RemoveMemberAsync(
            client,
            roomCode,
            deviceSessionId,
            hostToken);

        removeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await removeResponse.Content.ReadFromJsonAsync<RemoveMemberResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Error.ShouldBeNull();

        using var closeResponse = await RoomApiClient.CloseRoomAsync(client, roomCode, hostToken);
        closeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var rejoin = await RoomApiClient.JoinRoomAsync(client, roomCode, joinResult.SessionToken);
        rejoin.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var joinResultAgain = await rejoin.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        joinResultAgain!.Error!.Code.ShouldBe(HubErrorCode.RoomFull);
    }

    [Fact]
    public async Task RemoveMember_CannotRemoveHost_ReturnsConflict()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var createResponse = await RoomApiClient.CreateRoomAsync(client, Guid.NewGuid());
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);
        var roomCode = createResult!.RoomCode!;
        var hostDeviceSessionId = createResult.DeviceSessionId!.Value;
        await RoomApiClient.MarkReadyAsync(client, roomCode, createResult.SessionToken!);

        using var removeResponse = await RoomApiClient.RemoveMemberAsync(
            client,
            roomCode,
            hostDeviceSessionId,
            createResult.SessionToken!);

        removeResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var result = await removeResponse.Content.ReadFromJsonAsync<RemoveMemberResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Error!.Code.ShouldBe(HubErrorCode.CannotRemoveHost);
    }

    [Fact]
    public async Task RemoveMember_UnknownMember_ReturnsNotFound()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        var (roomCode, hostToken) = await CreateReadyRoomAsync(client);

        using var removeResponse = await RoomApiClient.RemoveMemberAsync(
            client,
            roomCode,
            Guid.NewGuid(),
            hostToken);

        removeResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var result = await removeResponse.Content.ReadFromJsonAsync<RemoveMemberResponse>(RoomApiClient.JsonOptions);
        result!.Error!.Code.ShouldBe(HubErrorCode.MemberNotFound);
    }

    [Fact]
    public async Task RemoveMember_MissingAuthorization_ReturnsUnauthorized()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        var (roomCode, _) = await CreateReadyRoomAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/rooms/{roomCode}/members/{Guid.NewGuid()}");
        request.Headers.Add(ApiKeyAuthenticationDefaults.HeaderName, HubApplicationFactory.ApiKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-configured-key")]
    public async Task RemoveMember_WithMissingOrInvalidApiKey_ReturnsUnauthorized(string? apiKey)
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.RemoveMemberAsync(
            client,
            "ABC234",
            Guid.NewGuid(),
            "any-token",
            apiKey);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain(HubApplicationFactory.ApiKey);
    }

    [Fact]
    public async Task MarkRoomReady_WhenAlreadyReady_ReturnsConflictInvalidRoomState()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        var (roomCode, hostToken) = await CreateReadyRoomAsync(client);

        using var response = await RoomApiClient.MarkReadyAsync(client, roomCode, hostToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var result = await response.Content.ReadFromJsonAsync<ReadyResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Error!.Code.ShouldBe(HubErrorCode.InvalidRoomState);
    }

    [Fact]
    public async Task RemoveMember_RoomNotFound_ReturnsNotFound()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.RemoveMemberAsync(
            client,
            "NOEXIST",
            Guid.NewGuid(),
            "any-token");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var result = await response.Content.ReadFromJsonAsync<RemoveMemberResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Error!.Code.ShouldBe(HubErrorCode.RoomNotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CloseRoom_EmptySessionToken_ReturnsValidationProblem(string? sessionToken)
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rooms/ABC234/close");
        if (sessionToken is not null)
        {
            request.Headers.Add("Session-Token", sessionToken);
        }
        request.Headers.Add(ApiKeyAuthenticationDefaults.HeaderName, HubApplicationFactory.ApiKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("Session-Token");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MarkRoomReady_EmptySessionToken_ReturnsValidationProblem(string? sessionToken)
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rooms/ABC234/ready");
        if (sessionToken is not null)
        {
            request.Headers.Add("Session-Token", sessionToken);
        }
        request.Headers.Add(ApiKeyAuthenticationDefaults.HeaderName, HubApplicationFactory.ApiKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("Session-Token");
    }

    [Fact]
    public async Task RemoveMember_WithSessionToken_ReturnsOk()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        var (roomCode, hostToken) = await CreateReadyRoomAsync(client);

        using var joinResponse = await RoomApiClient.JoinRoomAsync(client, roomCode, sessionToken: null);
        joinResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var joinResult = await joinResponse.Content.ReadFromJsonAsync<JoinResponse>(RoomApiClient.JsonOptions);
        var deviceSessionId = joinResult!.DeviceSessionId!.Value;

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/rooms/{roomCode}/members/{deviceSessionId}");
        request.Headers.Add("Session-Token", hostToken);
        request.Headers.Add(ApiKeyAuthenticationDefaults.HeaderName, HubApplicationFactory.ApiKey);

        using var removeResponse = await client.SendAsync(request);

        removeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await removeResponse.Content.ReadFromJsonAsync<RemoveMemberResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateRoom_EmptyGameId_ReturnsValidationProblem()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rooms");
        request.Content = JsonContent.Create(new CreateRoomRequest(Guid.Empty));
        request.Headers.Add(ApiKeyAuthenticationDefaults.HeaderName, HubApplicationFactory.ApiKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("GameId");
    }

    private static async Task<(string RoomCode, string HostToken)> CreateReadyRoomAsync(HttpClient client)
    {
        using var createResponse = await RoomApiClient.CreateRoomAsync(client, Guid.NewGuid());
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);
        var roomCode = createResult!.RoomCode!;
        await RoomApiClient.MarkReadyAsync(client, roomCode, createResult.SessionToken!);
        return (roomCode, createResult.SessionToken!);
    }
}
