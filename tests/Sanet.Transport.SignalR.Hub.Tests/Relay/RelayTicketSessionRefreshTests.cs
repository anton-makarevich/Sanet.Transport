using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Sanet.Transport.Relay.Contracts;
using Sanet.Transport.SignalR.Hub.Security;
using Shouldly;
using Xunit;

namespace Sanet.Transport.SignalR.Hub.Tests.Relay;

/// <summary>
/// Relay-ticket issuance must keep device sessions alive for as long as their room is
/// alive (issue #52): a mid-game disconnect after the original session window must still
/// allow the client to fetch a fresh relay ticket, while a session can never outlive its
/// expired or dissolved room.
/// </summary>
public class RelayTicketSessionRefreshTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task TicketRequest_AfterOriginalSessionExpiry_ButRoomAlive_SucceedsWithFreshTicket()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        await using var factory = new HubApplicationFactory(roomTtlSeconds: 60, timeProvider: clock);
        using var httpClient = factory.CreateClient();

        var created = (await CreateRoomAsync(httpClient))!;
        // Touch the room (MarkReady slides room expiry to t0+30+60=t0+90) while the
        // host session keeps its original expiry of t0+60.
        clock.Advance(TimeSpan.FromSeconds(30));
        (await RoomApiClient.MarkReady(httpClient, created.RoomCode!, created.SessionToken!))
            .EnsureSuccessStatusCode();

        // Advance past the original session expiry but before the slid room expiry.
        clock.Advance(TimeSpan.FromSeconds(31));

        var ticket = await RoomApiClient.RequestRelayTicket(
            httpClient, created.RoomCode!, created.SessionToken!);

        ticket.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TicketRequest_AfterRoomExpiry_IsRejected()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        await using var factory = new HubApplicationFactory(roomTtlSeconds: 60, timeProvider: clock);
        using var httpClient = factory.CreateClient();

        var created = (await CreateRoomAsync(httpClient))!;
        clock.Advance(TimeSpan.FromSeconds(61));

        var response = await RequestRelayTicketRaw(
            httpClient, created.RoomCode!, created.SessionToken!);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static async Task<CreateRoomResponse> CreateRoomAsync(HttpClient client)
    {
        using var createResponse = await RoomApiClient.CreateRoom(client, Guid.NewGuid());
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateRoomResponse>(JsonOptions);
        created.ShouldNotBeNull();
        return created;
    }

    private static async Task<HttpResponseMessage> RequestRelayTicketRaw(
        HttpClient client,
        string roomCode,
        string sessionToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/rooms/{roomCode}/relay-ticket");
        request.Headers.Add("Session-Token", sessionToken);
        request.Headers.Add(ApiKeyAuthenticationDefaults.HeaderName, HubApplicationFactory.ApiKey);
        return await client.SendAsync(request);
    }
}
