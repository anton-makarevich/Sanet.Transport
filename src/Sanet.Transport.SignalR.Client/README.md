# Sanet.Transport.SignalR.Client

Provides client-side transport publishers (`SignalRClientPublisher`, `RelayClientPublisher`) and network discovery services for the `Sanet.Transport` SignalR implementation. This package allows clients to connect to a SignalR host or a Cloud Relay Hub without requiring ASP.NET Core dependencies.

[![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.SignalR.Client?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport.SignalR.Client/)

## Overview

This library contains client transport implementations for `ITransportPublisher`:

- **`SignalRClientPublisher`**: Connects directly to a LAN `Sanet.Transport.SignalR.Server` embedded host (`SignalRHostManager`).
- **`RelayClientPublisher`**: Connects outbound to a Cloud `RelayHub` using WebSockets, room codes, and session tokens.
- **Network Discovery Services** (`BroadcastDiscoveryService`, `MulticastDiscoveryService`, `IDiscoveryService`): Allows clients to find SignalR hosts on the local network.

## Installation

```bash
dotnet add package Sanet.Transport.SignalR.Client
```

Or via the Package Manager Console:
```powershell
Install-Package Sanet.Transport.SignalR.Client
```

## Usage Examples

### 1. LAN Client (`SignalRClientPublisher` with UDP Discovery)

```csharp
using Sanet.Transport;
using Sanet.Transport.SignalR.Client.Discovery;
using Sanet.Transport.SignalR.Client.Publishers;

// 1. Discover hosts on LAN
using var discoveryService = new BroadcastDiscoveryService();
var discoveredUrls = await discoveryService.DiscoverHosts(timeoutSeconds: 5);

if (discoveredUrls.Count > 0)
{
    // 2. Connect to the host
    await using var publisher = new SignalRClientPublisher(discoveredUrls[0]);
    publisher.Subscribe(message => 
    {
        Console.WriteLine($"Received: {message.MessageType} from {message.SourceId}");
    });

    await publisher.StartAsync();

    // 3. Publish message
    await publisher.PublishMessage(new TransportMessage 
    {
        MessageType = "ClientHello",
        SourceId = Guid.NewGuid(),
        Payload = "{\"data\":\"hello\"}",
        Timestamp = DateTime.UtcNow
    });
}
```

### 2. Cloud Relay Client (`RelayClientPublisher`)

Used for cross-network/internet play without port forwarding. Connects outbound over WebSockets to a `RelayHub` using a room code and a session token issued by the room management REST API.

```csharp
using Sanet.Transport;
using Sanet.Transport.SignalR.Client.Publishers;
using Sanet.Transport.SignalR.Client.Relay;

// Session token received from room join/create REST API
string hubUrl = "wss://relay.example.com/relayhub";
string roomCode = "ABC234";
string sessionToken = "session-token-from-rest-api";

// Optional API key; required when the relay hub enforces relay authentication
// (RelayAuthenticationMiddleware). Hubs that don't require an api key can omit it.
string? apiKey = "api-key-from-relay-api";

await using var publisher = new RelayClientPublisher(hubUrl, roomCode, sessionToken, apiKey: apiKey);

// Optional: Listen for hub events
publisher.PeerConnected += peerId => Console.WriteLine($"Peer connected: {peerId}");
publisher.PeerDisconnected += peerId => Console.WriteLine($"Peer disconnected: {peerId}");
publisher.HubErrorReceived += error => Console.WriteLine($"Hub error: {error.Code} - {error.Message}");

// Subscribe to transport messages
publisher.Subscribe(message => 
{
    Console.WriteLine($"Received: {message.MessageType} from {message.SourceId}");
});

// Start WebSocket connection
await publisher.StartAsync();

// Publish message to the room
await publisher.PublishMessage(new TransportMessage 
{
    MessageType = "GameCommand",
    SourceId = myPlayerId,
    Payload = "{\"action\":\"move\"}",
    Timestamp = DateTime.UtcNow
});
```

## License

This project is licensed under the MIT License - see the [LICENSE](../../LICENSE) file for details.
