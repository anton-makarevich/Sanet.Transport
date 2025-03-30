# Sanet.Transport.SignalR

ASP.NET Core SignalR implementation of the Sanet.Transport abstractions for real-time communication over networks.

[![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.SignalR?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport.SignalR/)

## Overview

Sanet.Transport.SignalR provides an implementation of the `ITransportPublisher` interface using ASP.NET Core SignalR, enabling real-time communication between applications over a network. The library encapsulates all SignalR infrastructure, keeping client applications clean from web server dependencies.

## Features

- Real-time communication between applications over a network
- Client-server architecture with separate client and server publishers
- Automatic host discovery on simple local networks
- Self-contained SignalR infrastructure (no need to add ASP.NET Core to client apps)

## Installation

```
dotnet add package Sanet.Transport.SignalR
```

Or via the Package Manager Console:
```
Install-Package Sanet.Transport.SignalR
```

## Usage

### Server-side (Host)

```csharp
// Create a SignalR host (server)
var hostManager = await SignalRTransportFactory.CreateHostAsync(port: 5000);

// Get the server-side publisher
ITransportPublisher serverPublisher = hostManager.Publisher;

// Subscribe to messages from clients
serverPublisher.Subscribe(message => {
    Console.WriteLine($"Server received: {message.CommandType}");
    
    // Broadcast a response to all clients
    serverPublisher.PublishMessage(new TransportMessage {
        CommandType = "ServerResponse",
        SourceId = Guid.NewGuid(),
        Payload = "{\"server\": \"response\"}",
        Timestamp = DateTime.UtcNow
    });
});
```

### Client-side

```csharp
// Discover hosts on the network
var hosts = await discoveryService.DiscoverHostsAsync(timeoutSeconds: 5);

// Connect to the first discovered host
if (hosts.Count > 0)
{
    // Create a client publisher connected to the host
    ITransportPublisher clientPublisher = SignalRTransportFactory.CreateClient(hosts[0]);
    
    // Subscribe to messages from the server
    clientPublisher.Subscribe(message => {
        Console.WriteLine($"Client received: {message.CommandType}");
    });
    
    // Send a message to the server
    clientPublisher.PublishMessage(new TransportMessage {
        CommandType = "ClientCommand",
        SourceId = Guid.NewGuid(),
        Payload = "{\"client\": \"data\"}",
        Timestamp = DateTime.UtcNow
    });
}
```

### Manual Connection (Without Discovery)

```csharp
// Connect to a known host
var hostInfo = new HostInfo { Url = "http://192.168.1.100:5000" };
ITransportPublisher clientPublisher = SignalRTransportFactory.CreateClient(hostInfo);
```

## License

This project is licensed under the MIT License - see the LICENSE file for details.
