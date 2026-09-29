using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sanet.Transport.Relay.Contracts;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests.Rooms;

public class CreateRoomsEndpointTests
{
    [Fact]
    public async Task CreateRoom_WithValidApiKey_CreatesHostRoomAndSession()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();
        var gameInfo = new RoomGameInfo(
            Guid.NewGuid(),
            "MakaMek",
            "v0.64.0",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["rules"] = "standard" });

        using var response = await RoomApiClient.CreateRoom(client, gameInfo);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.GameInfo.ShouldNotBeNull();
        result.GameInfo!.HostId.ShouldBe(gameInfo.HostId);
        result.GameInfo.Id.ShouldBe("MakaMek");
        result.GameInfo.Version.ShouldBe("v0.64.0");
        result.GameInfo.Metadata.ShouldNotBeNull();
        result.GameInfo.Metadata!["rules"].ShouldBe("standard");
        result.DeviceSessionId.ShouldNotBeNull();
        result.DeviceSessionId.ShouldNotBe(Guid.Empty);
        result.Error.ShouldBeNull();
        result.RoomCode!.ShouldMatch("^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{6}$");
        string.IsNullOrWhiteSpace(result.SessionToken).ShouldBeFalse();
        result.ExpiresAt.ShouldNotBeNull();
        (result.ExpiresAt!.Value - DateTimeOffset.UtcNow).TotalMinutes.ShouldBeInRange(119, 121);
    }

    [Fact]
    public async Task CreateRoom_AtConfiguredCapacity_ReturnsHubAtCapacityAndActiveRoomCount()
    {
        await using var factory = new HubApplicationFactory(maxConcurrentRooms: 1);
        using var client = factory.CreateClient();

        using var firstResponse = await RoomApiClient.CreateRoom(client, Guid.NewGuid());
        using var secondResponse = await RoomApiClient.CreateRoom(client, Guid.NewGuid());

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var result = await secondResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);

        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error!.Code.ShouldBe(HubErrorCode.HubAtCapacity);
        result.Error.ActiveRoomCount.ShouldBe(1);
    }

    [Fact]
    public async Task RequestToNonApiPath_PassesThroughWithoutAuthentication()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");

        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateRoom_WithEmptyHostId_ReturnsValidationError()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.CreateRoom(
            client,
            new RoomGameInfo(Guid.Empty, "MakaMek", "v0.64.0"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("HostId");
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("errors")
            .TryGetProperty("GameInfo.HostId", out var fieldErrors)
            .ShouldBeTrue();
        fieldErrors.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task CreateRoom_WithInvalidGameId_ReturnsValidationError()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.CreateRoom(
            client,
            new RoomGameInfo(Guid.NewGuid(), "not a valid id", "v0.64.0"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errors")
            .TryGetProperty("GameInfo.Id", out var fieldErrors)
            .ShouldBeTrue();
        fieldErrors.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task CreateRoom_WithoutGameInfo_ReturnsValidationError()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rooms");
        request.Content = JsonContent.Create(new { });
        request.Headers.Add("X-Api-Key", HubApplicationFactory.ApiKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errors")
            .TryGetProperty("GameInfo", out var fieldErrors)
            .ShouldBeTrue();
        fieldErrors.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task CreateRoom_WithoutMetadata_SucceedsAndReturnsEmptyMetadata()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.CreateRoom(
            client,
            new RoomGameInfo(Guid.NewGuid(), "MakaMek", "v0.64.0"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);

        result.ShouldNotBeNull();
        result.GameInfo.ShouldNotBeNull();
        result.GameInfo!.Id.ShouldBe("MakaMek");
        result.GameInfo.Metadata.ShouldNotBeNull();
        result.GameInfo.Metadata.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateRoom_PreservesMetadataKeyCasing()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rules"] = "standard",
            ["techLevel"] = "introductory"
        };

        using var response = await RoomApiClient.CreateRoom(
            client,
            new RoomGameInfo(Guid.NewGuid(), "MakaMek", "v0.64.0", metadata));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var returnedMetadata = body.RootElement.GetProperty("gameInfo").GetProperty("metadata");

        returnedMetadata.TryGetProperty("rules", out _).ShouldBeTrue();
        returnedMetadata.TryGetProperty("techLevel", out _).ShouldBeTrue();
        returnedMetadata.TryGetProperty("techlevel", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task CreateRoom_WithTooManyMetadataEntries_ReturnsValidationError()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i <= RoomGameInfoLimits.MaxMetadataEntries; i++)
        {
            metadata[$"key{i}"] = "value";
        }

        using var response = await RoomApiClient.CreateRoom(
            client,
            new RoomGameInfo(Guid.NewGuid(), "MakaMek", "v0.64.0", metadata));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errors")
            .TryGetProperty("GameInfo.Metadata", out var fieldErrors)
            .ShouldBeTrue();
        fieldErrors.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-configured-key")]
    public async Task CreateRoom_WithMissingOrInvalidApiKey_IsRejectedWithoutLeakingConfiguredKey(string? apiKey)
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.CreateRoom(client, Guid.NewGuid(), apiKey);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain(HubApplicationFactory.ApiKey);
    }
}
