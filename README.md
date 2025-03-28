# Sanet.Transport

A lightweight transport layer for message passing between distributed systems.

[![Build Status](https://github.com/anton-makarevich/Sanet.Transport/actions/workflows/transport.yml/badge.svg)](https://github.com/anton-makarevich/Sanet.Transport/actions/workflows/transport.yml)
[![codecov](https://codecov.io/gh/anton-makarevich/Sanet.Transport/branch/main/graph/badge.svg)](https://codecov.io/gh/anton-makarevich/Sanet.Transport)

| Package | Version                                                                                                                                |
| ------- |----------------------------------------------------------------------------------------------------------------------------------------|
| Sanet.Transport | [![NuGet](https://img.shields.io/nuget/v/Sanet.Transport?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport/)                 |
| Sanet.Transport.Rx | [![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.Rx?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport.Rx/)           |
| Sanet.Transport.Channel | [![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.Channel?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport.Channel/) |
| Sanet.Transport.SignalR | [![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.SignalR?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport.SignalR/) |

## Overview

Sanet.Transport provides a simple, extensible architecture for publishing and subscribing to messages between different parts of an application or between distributed systems. It's designed to be independent of any specific game or application logic, making it reusable across different projects.

## Key Features

- **Decoupled Communication**: Enables communication between components without direct dependencies
- **Multiple Transport Implementations**:
  - **Rx**: Using Reactive Extensions for reactive programming patterns
  - **Channel**: Using System.Threading.Channels for high-performance message passing
  - **SignalR**: Using ASP.NET Core SignalR for real-time communication between applications over a network
- **Simple API**: Easy to use publisher/subscriber pattern
- **Extensible**: Create custom transport implementations for specific needs

## Getting Started

### Installation

#### Using NuGet Packages (Recommended)

Install the core package:
```
dotnet add package Sanet.Transport
```

For Reactive Extensions implementation:
```
dotnet add package Sanet.Transport.Rx
```

For Channels implementation:
```
dotnet add package Sanet.Transport.Channel
```

For SignalR implementation:
```
dotnet add package Sanet.Transport.SignalR
```

Or via the Package Manager Console:
```
Install-Package Sanet.Transport
Install-Package Sanet.Transport.Rx
Install-Package Sanet.Transport.Channel
Install-Package Sanet.Transport.SignalR
```

#### Using Project References (Alternative)

If you prefer to reference the projects directly:

```
dotnet add reference path/to/Sanet.Transport.csproj
```

For Reactive Extensions implementation:
```
dotnet add reference path/to/Sanet.Transport.Rx.csproj
```

For Channels implementation:
```
dotnet add reference path/to/Sanet.Transport.Channel.csproj
```

For SignalR implementation:
```
dotnet add reference path/to/Sanet.Transport.SignalR.csproj
```

### Basic Usage

```csharp
// Create a transport publisher
ITransportPublisher publisher = new RxTransportPublisher(); // or new ChannelTransportPublisher()

// Subscribe to messages
publisher.Subscribe(message => {
    Console.WriteLine($"Received message: {message.CommandType}");
    // Process the message
});

// Publish a message
publisher.PublishMessage(new TransportMessage {
    CommandType = "SomeCommand",
    SourceId = Guid.NewGuid(),
    Payload = "{\"key\": \"value\"}",
    Timestamp = DateTime.UtcNow
});
```

### SignalR Usage

The SignalR implementation allows for real-time communication between applications over a network:

```csharp
// Create a SignalR host (server)
var hostManager = await SignalRTransportFactory.CreateHostAsync(
    port: 5000,
    enableDiscovery: true);

// Get the server-side publisher
ITransportPublisher serverPublisher = hostManager.Publisher;

// In another application, discover hosts on the network
var hosts = await SignalRTransportFactory.DiscoverHostsAsync(timeoutSeconds: 5);

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

## Project Structure

- **Sanet.Transport**: Core interfaces and message definitions
- **Sanet.Transport.Rx**: Implementation using Reactive Extensions
- **Sanet.Transport.Channel**: Implementation using System.Threading.Channels
- **Sanet.Transport.SignalR**: Implementation using ASP.NET Core SignalR for network communication
- **Sanet.Transport.Rx.Tests**: Unit tests for Rx implementation
- **Sanet.Transport.Channel.Tests**: Unit tests for Channel implementation
- **Sanet.Transport.SignalR.Tests**: Unit tests for SignalR implementation

## License

This project is licensed under the MIT License - see the LICENSE file for details.
