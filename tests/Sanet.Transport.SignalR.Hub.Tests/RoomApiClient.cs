using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sanet.Transport.SignalR.Hub.Contracts;
using Sanet.Transport.SignalR.Hub.Security;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests;

/// <summary>
/// Shared HTTP helpers for exercising the room-management REST endpoints in integration tests.
/// API keys default to <see cref="HubApplicationFactory.ApiKey"/>; pass null to omit the header.
/// </summary>
internal static class RoomApiClient
{
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public static async Task<HttpResponseMessage> CreateRoom(
        HttpClient client,
        Guid gameId,
        string? apiKey = HubApplicationFactory.ApiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rooms");
        request.Content = JsonContent.Create(new CreateRoomRequest(gameId));
        AddApiKey(request, apiKey);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> JoinRoom(
        HttpClient client,
        string roomCode,
        string? sessionToken,
        string? apiKey = HubApplicationFactory.ApiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/join");
        if (sessionToken is not null)
        {
            request.Headers.Add("Session-Token", sessionToken);
        }

        AddApiKey(request, apiKey);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> MarkReady(
        HttpClient client,
        string roomCode,
        string sessionToken,
        string? apiKey = HubApplicationFactory.ApiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/ready");
        request.Headers.Add("Session-Token", sessionToken);
        AddApiKey(request, apiKey);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> LockRoom(
        HttpClient client,
        string roomCode,
        string sessionToken,
        string? apiKey = HubApplicationFactory.ApiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/lock");
        request.Headers.Add("Session-Token", sessionToken);
        AddApiKey(request, apiKey);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> RemoveMember(
        HttpClient client,
        string roomCode,
        Guid deviceSessionId,
        string sessionToken,
        string? apiKey = HubApplicationFactory.ApiKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/rooms/{roomCode}/members/{deviceSessionId}");
        request.Headers.Add("Session-Token", sessionToken);
        AddApiKey(request, apiKey);
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Requests a relay ticket from <c>POST /api/rooms/{roomCode}/relay-ticket</c> and returns
    /// the ticket value, asserting a successful response.
    /// </summary>
    public static async Task<string> RequestRelayTicket(
        HttpClient client,
        string roomCode,
        string sessionToken,
        string? apiKey = HubApplicationFactory.ApiKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/rooms/{roomCode}/relay-ticket");
        request.Headers.Add("Session-Token", sessionToken);
        AddApiKey(request, apiKey);
        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ticket = await response.Content.ReadFromJsonAsync<RelayTicketResponse>(JsonOptions);
        ticket.ShouldNotBeNull();
        ticket.Success.ShouldBeTrue();
        ticket.Ticket.ShouldNotBeNull();
        return ticket.Ticket;
    }

    private static void AddApiKey(HttpRequestMessage request, string? apiKey)
    {
        if (apiKey is not null)
        {
            request.Headers.Add(ApiKeyAuthenticationDefaults.HeaderName, apiKey);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
