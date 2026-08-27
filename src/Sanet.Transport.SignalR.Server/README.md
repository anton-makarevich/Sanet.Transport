# Sanet.Transport.SignalR.Server

Provides the server-side components (SignalR Hub, Host Manager, Server Publisher) for the Sanet.Transport SignalR implementation. This package includes the necessary ASP.NET Core dependencies to host a SignalR hub.

[![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.SignalR.Server?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport.SignalR.Server/)

## Overview

This library contains the server infrastructure required to host a SignalR hub for `Sanet.Transport`. It uses `Microsoft.NET.Sdk.Web` and includes:

- `TransportHub`: The core SignalR Hub.
- `SignalRHostManager`: Manages a self-contained SignalR host.
- `SignalRServerPublisher`: Implements `ITransportPublisher` for the server-side, broadcasting messages to connected clients.

**Note:** This package depends on `Sanet.Transport.SignalR.Client` for network discovery broadcasting functionality.

## Features

- Host a self-contained SignalR Hub.
- Manage the SignalR host lifecycle.
- Broadcast `TransportMessage` objects to all connected clients.
- Receive messages from clients.
- Integrates with network discovery (via `Sanet.Transport.SignalR.Client`).

## Installation

```
dotnet add package Sanet.Transport.SignalR.Server
```

Or via the Package Manager Console:
```
Install-Package Sanet.Transport.SignalR.Server
```

## Usage

### Server-side (Host)

```csharp
// Create a SignalR host (server)
var hostManager = await SignalRTransportFactory.CreateHostAsync(port: 5000);

// Get the server-side publisher
ITransportPublisher serverPublisher = hostManager.Publisher;

// (Optional) Make the host discoverable using the discovery service from the Client package
// var discoveryService = new Sanet.Transport.SignalR.Client.Discovery.BroadcastDiscoveryService(); 
// discoveryService.BroadcastPresence(hostManager.HubUrl); 

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

## Connection State

`ConnectionState` reports whether the **host publisher** is active: `Connected` while running
and `Closed` after disposal (with `ConnectionStateChanged` firing once). It does **not**
indicate whether any client is attached — a running host is `Connected` even with zero
clients, and clients joining/leaving never change the reported state.

## License

This project is licensed under the MIT License - see the LICENSE file for details.
