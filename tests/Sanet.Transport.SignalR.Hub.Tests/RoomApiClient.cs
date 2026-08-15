using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sanet.Transport.SignalR.Hub.Contracts;
using Sanet.Transport.SignalR.Hub.Security;

namespace Sanet.Transport.SignalR.Hub.Tests;

/// <summary>
/// Shared HTTP helpers for exercising the room-management REST endpoints in integration tests.
/// API keys default to <see cref="HubApplicationFactory.ApiKey"/>; pass null to omit the header.
/// </summary>
internal static class RoomApiClient
{
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public static async Task<HttpResponseMessage> CreateRoomAsync(
        HttpClient client,
        Guid gameId,
        string? apiKey = HubApplicationFactory.ApiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rooms");
        request.Content = JsonContent.Create(new CreateRoomRequest(gameId));
        AddApiKey(request, apiKey);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> JoinRoomAsync(
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

    public static async Task<HttpResponseMessage> MarkReadyAsync(
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

    public static async Task<HttpResponseMessage> CloseRoomAsync(
        HttpClient client,
        string roomCode,
        string sessionToken,
        string? apiKey = HubApplicationFactory.ApiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/close");
        request.Headers.Add("Session-Token", sessionToken);
        AddApiKey(request, apiKey);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> RemoveMemberAsync(
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
