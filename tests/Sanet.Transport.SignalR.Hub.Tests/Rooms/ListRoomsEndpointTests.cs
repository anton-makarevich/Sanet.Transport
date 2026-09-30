using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sanet.Transport.Relay.Contracts;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests.Rooms;

public class ListRoomsEndpointTests
{
    [Fact]
    public async Task ListRooms_WithoutApiKey_ReturnsUnauthorized()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.ListRooms(client, apiKey: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListRooms_WithoutGameId_ReturnsValidationError()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.ListRooms(client, gameId: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("errors")
            .TryGetProperty("gameId", out var fieldErrors)
            .ShouldBeTrue();
        fieldErrors.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ListRooms_WithBlankGameId_ReturnsValidationError()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.ListRooms(client, gameId: "   ");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("gameId");
    }

    [Fact]
    public async Task ListRooms_WithInvalidGameId_ReturnsValidationError()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.ListRooms(client, gameId: "not a valid id");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListRooms_ListsRoomDirectlyAfterCreationBeforeReadiness()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var createResponse = await RoomApiClient.CreateRoom(
            client,
            RoomApiClient.CreateGameInfo());
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var response = await RoomApiClient.ListRooms(client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ListRoomsResponse>(RoomApiClient.JsonOptions);
        result.ShouldNotBeNull();
        result.Rooms.Count.ShouldBe(1);
        result.Rooms[0].RoomCode.ShouldNotBeNull();
        result.Rooms[0].MemberCount.ShouldBe(1);
        result.Rooms[0].GameInfo.ShouldNotBeNull();
        result.Rooms[0].GameInfo.Id.ShouldBe(RoomApiClient.DefaultGameId);
        result.Rooms[0].GameInfo.Version.ShouldBe(RoomApiClient.DefaultGameVersion);
    }

    [Fact]
    public async Task ListRooms_ListsActiveRoomsAPlayerCanJoinAfterReadiness()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var createResponse = await RoomApiClient.CreateRoom(client, RoomApiClient.CreateGameInfo());
        var created = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);
        using var readyResponse = await RoomApiClient.MarkReady(
            client,
            created!.RoomCode!,
            created.SessionToken!);
        readyResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var response = await RoomApiClient.ListRooms(client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ListRoomsResponse>(RoomApiClient.JsonOptions);
        result!.Rooms.Count.ShouldBe(1);
        result.Rooms[0].RoomCode.ShouldBe(created.RoomCode);
    }

    [Fact]
    public async Task ListRooms_AfterLock_ExcludesRoomFromRoomsAPlayerCanJoin()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var createResponse = await RoomApiClient.CreateRoom(client, RoomApiClient.CreateGameInfo());
        var created = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);
        await RoomApiClient.MarkReady(client, created!.RoomCode!, created.SessionToken!);
        await RoomApiClient.LockRoom(client, created.RoomCode!, created.SessionToken!);

        using var response = await RoomApiClient.ListRooms(client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ListRoomsResponse>(RoomApiClient.JsonOptions);
        result!.Rooms.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListRooms_WithVersionFilter_ListsOnlyMatchingRoomsAPlayerCanJoin()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var matching = await RoomApiClient.CreateRoom(
            client,
            RoomApiClient.CreateGameInfo(version: "v0.64.0"));
        using var other = await RoomApiClient.CreateRoom(
            client,
            RoomApiClient.CreateGameInfo(version: "v1.0.0"));

        using var response = await RoomApiClient.ListRooms(client, version: "v0.64.0");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ListRoomsResponse>(RoomApiClient.JsonOptions);
        result!.Rooms.Count.ShouldBe(1);
        result.Rooms[0].RoomCode.ShouldBe(
            (await matching.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions))!.RoomCode);
    }

    [Fact]
    public async Task ListRooms_WithMetadataFilter_ListsOnlyMatchingRoomsAPlayerCanJoin()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();
        var matchingGameInfo = new RoomGameInfo(
            Guid.NewGuid(),
            RoomApiClient.DefaultGameId,
            RoomApiClient.DefaultGameVersion,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["map.name"] = "forest",
                ["tech_level"] = "intro",
                ["rules-beta"] = "standard"
            });
        using var createResponse = await RoomApiClient.CreateRoom(client, matchingGameInfo);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var response = await RoomApiClient.ListRooms(
            client,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["map.name"] = "forest",
                ["tech_level"] = "intro",
                ["rules-beta"] = "standard"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ListRoomsResponse>(RoomApiClient.JsonOptions);
        result!.Rooms.Count.ShouldBe(1);
        result.Rooms[0].GameInfo.ShouldNotBeNull();
        result.Rooms[0].GameInfo.Metadata!["map.name"].ShouldBe("forest");
    }

    [Fact]
    public async Task ListRooms_WithNonMatchingMetadataFilter_ListsNoRoomsAPlayerCanJoin()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        await RoomApiClient.CreateRoom(client, RoomApiClient.CreateGameInfo());

        using var response = await RoomApiClient.ListRooms(
            client,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["map.name"] = "desert" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ListRoomsResponse>(RoomApiClient.JsonOptions);
        result!.Rooms.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListRooms_WithCaseDistinctMetadataFilterKeys_AppliesBothFilters()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();
        using var bothKeysCreated = await RoomApiClient.CreateRoom(client, new RoomGameInfo(
            Guid.NewGuid(),
            RoomApiClient.DefaultGameId,
            RoomApiClient.DefaultGameVersion,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Rules"] = "standard",
                ["rules"] = "beta"
            }));
        bothKeysCreated.StatusCode.ShouldBe(HttpStatusCode.Created);

        await RoomApiClient.CreateRoom(client, new RoomGameInfo(
            Guid.NewGuid(),
            RoomApiClient.DefaultGameId,
            RoomApiClient.DefaultGameVersion,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Rules"] = "standard" }));

        using var response = await RoomApiClient.ListRooms(
            client,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Rules"] = "standard",
                ["rules"] = "beta"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ListRoomsResponse>(RoomApiClient.JsonOptions);
        result!.Rooms.Count.ShouldBe(1);
        result.Rooms[0].RoomCode.ShouldBe(
            (await bothKeysCreated.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions))!.RoomCode);
    }

    [Fact]
    public async Task ListRooms_ResponseCarriesNoSessionOrDeviceSessionData()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        await RoomApiClient.CreateRoom(client, RoomApiClient.CreateGameInfo());

        using var response = await RoomApiClient.ListRooms(client);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("sessionToken");
        body.ShouldNotContain("SessionToken");
        body.ShouldNotContain("deviceSessionId");
        body.ShouldNotContain("DeviceSessionId");
        body.ShouldNotContain("\"role\"");
        body.ShouldNotContain("\"error\"");
    }

    [Fact]
    public async Task ListRooms_WithNoMatchingRooms_ReturnsEmptySuccess()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.ListRooms(client, gameId: "NoMatch");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ListRoomsResponse>(RoomApiClient.JsonOptions);
        result!.Rooms.ShouldBeEmpty();
    }
}
