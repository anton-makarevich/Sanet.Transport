# Sanet.Transport.SignalR.Client

Provides the client-side components (Client Publisher, Network Discovery) for the Sanet.Transport SignalR implementation. This package allows clients to connect to a SignalR host without needing ASP.NET Core dependencies.

[![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.SignalR.Client?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport.SignalR.Client/)

## Overview

This library contains the client infrastructure required to connect to a `Sanet.Transport.SignalR.Server` host. It uses `Microsoft.NET.Sdk` and includes:

- `SignalRClientPublisher`: Implements `ITransportPublisher` for the client-side, connecting to a specific SignalR hub.
- Network Discovery Services (`BroadcastDiscoveryService`, `MulticastDiscoveryService`, `IDiscoveryService`): Allows clients to find SignalR hosts on the local network.
- Network Utilities (`IUdpClientWrapper`, etc.): Low-level UDP helpers for discovery.

## Features

- Connect to a SignalR Hub hosted by `Sanet.Transport.SignalR.Server`.
- Send `TransportMessage` objects to the server.
- Receive messages broadcast by the server.
- Discover compatible SignalR hosts on the local network (Broadcast/Multicast).
- Does *not* require ASP.NET Core dependencies.

## Installation

```
dotnet add package Sanet.Transport.SignalR.Client
```

Or via the Package Manager Console:
```
Install-Package Sanet.Transport.SignalR.Client
```

## Usage

### Client-side (With Discovery)

```csharp
using Sanet.Transport.SignalR.Client.Discovery;
using Sanet.Transport.SignalR.Client.Publishers;
using Sanet.Transport;

// Create a discovery service (use Broadcast or Multicast)
using var discoveryService = new BroadcastDiscoveryService();

// Discover hosts on the network
var discoveredUrls = await discoveryService.DiscoverHosts(timeoutSeconds: 5);

ITransportPublisher? clientPublisher = null;

// Connect to the first discovered host
if (discoveredUrls.Count > 0)
{
    Console.WriteLine($"Found host at {discoveredUrls[0]}");
    // Create a client publisher connected to the host
    clientPublisher = new SignalRClientPublisher(discoveredUrls[0]);
    await ((SignalRClientPublisher)clientPublisher).StartAsync(); // Start the connection
    
    // Subscribe to messages from the server
    clientPublisher.Subscribe(message => {
        Console.WriteLine($"Client received: {message.CommandType} from {message.SourceId}");
    });
    
    // Send a message to the server
    Console.WriteLine("Sending message to server...");
    clientPublisher.PublishMessage(new TransportMessage {
        CommandType = "ClientHello",
        SourceId = Guid.NewGuid(), // Client's ID
        Payload = "{\"client\": \"greetings\"}",
        Timestamp = DateTime.UtcNow
    });
} else {
    Console.WriteLine("No hosts found.");
}

Console.WriteLine("Client running. Press Enter to stop...");
Console.ReadLine();

// Dispose the publisher when done
if (clientPublisher is IAsyncDisposable asyncDisposable)
{
    await asyncDisposable.DisposeAsync();
}
else if (clientPublisher is IDisposable disposable)
{
    disposable.Dispose();
}

```

### Client-side (Manual Connection - Without Discovery)

```csharp
using Sanet.Transport.SignalR.Client.Publishers;
using Sanet.Transport;

// Connect to a known host URL
var hubUrl = "http://<server-ip-or-hostname>:5000/transporthub"; // Replace with actual URL
ITransportPublisher clientPublisher = new SignalRClientPublisher(hubUrl);
await ((SignalRClientPublisher)clientPublisher).StartAsync(); // Start the connection

// Subscribe and publish messages as shown in the discovery example...

// Remember to dispose the publisher when done
// ... (Dispose code as above) ...
```

## License

This project is licensed under the MIT License - see the LICENSE file for details.
