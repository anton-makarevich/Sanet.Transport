using System.Net;
using System.Net.Http.Json;
using Sanet.Transport.SignalR.Hub.Contracts;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests.Rooms;

public class CreateRoomsEndpointTests
{
    [Fact]
    public async Task CreateRoom_WithValidApiKey_CreatesHostRoomAndSession()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();
        var hostGameId = Guid.NewGuid();

        using var response = await RoomApiClient.CreateRoomAsync(client, hostGameId);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<CreateRoomResponse>(RoomApiClient.JsonOptions);

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.HostGameId.ShouldBe(hostGameId);
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

        using var firstResponse = await RoomApiClient.CreateRoomAsync(client, Guid.NewGuid());
        using var secondResponse = await RoomApiClient.CreateRoomAsync(client, Guid.NewGuid());

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
    public async Task CreateRoom_WithEmptyGameId_ReturnsValidationError()
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.CreateRoomAsync(client, Guid.Empty);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("GameId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-configured-key")]
    public async Task CreateRoom_WithMissingOrInvalidApiKey_IsRejectedWithoutLeakingConfiguredKey(string? apiKey)
    {
        await using var factory = new HubApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await RoomApiClient.CreateRoomAsync(client, Guid.NewGuid(), apiKey);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain(HubApplicationFactory.ApiKey);
    }
}
