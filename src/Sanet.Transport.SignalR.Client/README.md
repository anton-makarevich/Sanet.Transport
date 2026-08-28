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
using Microsoft.Extensions.Logging;
using Sanet.Transport;
using Sanet.Transport.SignalR.Client.Publishers;
using Sanet.Transport.SignalR.Client.Relay;

// Session token received from room join/create REST API
string hubUrl = "wss://relay.example.com/relayhub";
string roomCode = "ABC234";
string sessionToken = "session-token-from-rest-api";

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

await using var publisher = new RelayClientPublisher(
    hubUrl, roomCode, sessionToken, loggerFactory.CreateLogger<RelayClientPublisher>());

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

### 3. Automatic recovery with ticket refresh (`RelayClientPublisher`)

Relay tickets are short-lived. Without extra configuration, a connection drop after the
ticket window has passed is **terminal**: `Closed` fires and callers must recreate the
publisher. For games that must survive mid-session network drops (mobile doze, radio
handoffs), supply a `TicketRefresh` delegate: when the connection closes, the publisher
invokes it to obtain a fresh relay ticket, transparently rebuilds and restarts the
underlying SignalR connection (preserving subscribers and public events), flushes any
queued outbound messages, and raises `Reconnected`.

```csharp
using Sanet.Transport.SignalR.Client.Factories;
using Sanet.Transport.SignalR.Client.Publishers;

// Fetch the initial relay ticket from the REST API using the stored session token.
var initialResponse = await relayRoomClient.GetRelayTicket(roomCode, sessionToken, CancellationToken.None);
var relayTicket = initialResponse.Ticket!;

var options = new RelayPublisherOptions
{
    HubUrl = hubUrl,
    RoomCode = roomCode,
    RelayTicket = relayTicket,
    TicketRefresh = async ct =>
    {
        // Fetch a fresh relay ticket from the REST API using the stored session token.
        var response = await relayRoomClient.GetRelayTicket(roomCode, sessionToken, ct);
        return response.Success
            ? new RelayTicketRefresh(response.Ticket!, response.ExpiresAt)
            : null; // null (or a thrown exception) makes the close terminal -> Closed fires
    }
};

var factory = new RelayPublisherFactory(loggerFactory);
await using var publisher = await factory.Create(options);

publisher.Reconnected += connectionId =>
    Console.WriteLine($"Recovered with fresh ticket, connection {connectionId}");
publisher.Closed += error =>
    Console.WriteLine($"Terminal close — recreate the publisher with a fresh ticket");
```

Semantics:

- **`Closed`** is raised only when no recovery path exists (no ticket-expiry reconnect window
  and no refresh delegate), the delegate fails or returns null, or the bounded restart attempts
  are exhausted — i.e. it is truly terminal.
- **Unified outbound pipeline**: every message published via `PublishMessage` takes the same
  path. While the connection is connected it is sent immediately; while the connection is
  recovering (automatic reconnect or ticket-refresh rebuild) it is held in a bounded FIFO
  (500 messages by default) and delivered in order once connectivity returns — there is no
  separate "queued vs direct" send path, so ordering can never be violated by a recovery.
- **Delivery-completion**: the task returned by `PublishMessage` completes when the message has
  actually been handed to the transport (or faults when delivery definitively failed), so
  awaiting callers get truthful backpressure across recovery windows.
- **Failure modes**: `PublishMessage` throws `TransportPublishException` with
  `PublishFailureReason.QueueFull` synchronously when the pipeline is full, and faults awaiting
  callers with `PublishFailureReason.NotConnected` after a terminal close or when the publisher
  is disconnected with no recovery path configured. Catch `TransportPublishException` and retry
  as appropriate for your application.
- **Events** (`Reconnecting`, `Reconnected`, `Closed`, peer/host events) are dispatched
  asynchronously off the raising thread — via the `SynchronizationContext` captured at
  construction when one exists. Event handlers may call `PublishMessage` reentrantly; ordering
  of such messages relative to any recovery backlog is preserved by the pipeline's FIFO.
- **Breaking change**: publishing while not connected used to throw
  `InvalidOperationException("Relay client is not connected.")`; it now throws
  `TransportPublishException(PublishFailureReason.NotConnected)`.

### 4. Connection state

Both publishers implement `ITransportPublisher.ConnectionState` (a `TransportConnectionState`)
and raise `ConnectionStateChanged` on every transition. This state tracks **transport
connectivity only** — it is not raised when peers or the host join or leave the room (use the
room events `PeerConnected`, `PeerDisconnected`, `HostDisconnected` for that).

`SignalRClientPublisher` (LAN) mapping to the underlying `HubConnectionState`:

| TransportConnectionState | When reported |
| --- | --- |
| `Disconnected` | before `StartAsync` |
| `Connecting` | while `StartAsync` is establishing the connection |
| `Connected` | after the connection is established (and again after an automatic reconnect) |
| `Reconnecting` | while the connection is being re-established after an unexpected drop |
| `Closed` | after the connection is stopped/disposed |

`RelayClientPublisher` mapping:

| TransportConnectionState | When reported |
| --- | --- |
| `Connected` | after start completes, and again after every recovery |
| `Reconnecting` | while the underlying connection is in the ticket-expiry auto-reconnect window |
| `Disconnected` | while idle or parked during a ticket-refresh rebuild |
| `Closed` | **terminal** — the drop could not be recovered (no window / no refresh), the refresh delegate failed or returned null, or the publisher was disposed |

`Closed` is terminal for both publishers: after it is raised the publisher cannot reconnect,
so callers must construct a new publisher (fetch a fresh relay ticket for the relay variant)
before continuing.

**Breaking change**: `ITransportPublisher` now declares `ConnectionState` and
`ConnectionStateChanged`; custom implementations must add them to compile against this version.

## License

This project is licensed under the MIT License - see the [LICENSE](../../LICENSE) file for details.
