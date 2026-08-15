# Sanet.Transport

A lightweight transport layer for message passing between distributed systems.

[![Build Status](https://github.com/anton-makarevich/Sanet.Transport/actions/workflows/transport.yml/badge.svg)](https://github.com/anton-makarevich/Sanet.Transport/actions/workflows/transport.yml)
[![Hub Build Status](https://github.com/anton-makarevich/Sanet.Transport/actions/workflows/hub.yml/badge.svg)](https://github.com/anton-makarevich/Sanet.Transport/actions/workflows/hub.yml)
[![codecov](https://codecov.io/gh/anton-makarevich/Sanet.Transport/branch/main/graph/badge.svg)](https://codecov.io/gh/anton-makarevich/Sanet.Transport)

| Package                      | Version                                                                                                                                |
|------------------------------|----------------------------------------------------------------------------------------------------------------------------------------|
| Sanet.Transport              | [![NuGet](https://img.shields.io/nuget/v/Sanet.Transport?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport/)                 |
| Sanet.Transport.Rx           | [![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.Rx?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport.Rx/)           |
| Sanet.Transport.Channel      | [![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.Channel?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport.Channel/) |
| Sanet.Transport.SignalR.Client | [![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.SignalR.Client?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport.SignalR.Client/) |
| Sanet.Transport.SignalR.Server | [![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.SignalR.Server?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport.SignalR.Server/) |
| Sanet.Transport.SignalR.Hub  | [![Docker Image](https://img.shields.io/badge/Docker-Container-blue?logo=docker)](https://github.com/anton-makarevich/Sanet.Transport/pkgs/container/sanet.transport%2Fhub) |

## Overview

Sanet.Transport provides a simple, extensible architecture for publishing and subscribing to messages between different parts of an application or between distributed systems. It's designed to be independent of any specific game or application logic, making it reusable across different projects.

## Key Features

- **Decoupled Communication**: Enables communication between components without direct dependencies
- **Multiple Transport Implementations**:
  - **Rx**: Using Reactive Extensions for reactive programming patterns
  - **Channel**: Using System.Threading.Channels for high-performance message passing
  - **SignalR**: Real-time networking via ASP.NET Core SignalR:
    - **SignalRClientPublisher**: Direct LAN peer-to-host communication
    - **RelayClientPublisher**: Outbound cloud relay communication (NAT-traversal & cross-network play)
- **Simple API**: Easy to use publisher/subscriber pattern
- **Extensible**: Create custom transport implementations for specific needs

## Getting Started

### Installation

#### Using NuGet Packages (Recommended)

Install the core package:
```bash
dotnet add package Sanet.Transport
```

For specific implementations, install the corresponding package:
```bash
dotnet add package Sanet.Transport.Rx
dotnet add package Sanet.Transport.Channel
dotnet add package Sanet.Transport.SignalR.Client
dotnet add package Sanet.Transport.SignalR.Server
```

## Relay Hub Server

`Sanet.Transport.SignalR.Hub` is a self-hosted cloud relay service that backs `RelayClientPublisher`. It hosts a SignalR hub for real-time message relay and a REST API for room lifecycle management (create, join, ready, close, kick). It is not a NuGet package — run it directly or via Docker.

- See [`src/Sanet.Transport.SignalR.Hub/README.md`](src/Sanet.Transport.SignalR.Hub/README.md) for configuration, local run, and Docker instructions.

## SignalR Client Publishers

`Sanet.Transport.SignalR.Client` includes two implementations of `ITransportPublisher`:

### 1. `SignalRClientPublisher` (LAN Peer-to-Host)
Used for local network play where one peer acts as the embedded server host (`SignalRHostManager`).

```csharp
using Sanet.Transport;
using Sanet.Transport.SignalR.Client.Publishers;

var publisher = new SignalRClientPublisher("http://192.168.1.100:5000/transporthub");
publisher.Subscribe(message => Console.WriteLine($"Received: {message.MessageType}"));

await publisher.StartAsync();

await publisher.PublishMessage(new TransportMessage 
{ 
    MessageType = "GameCommand", 
    SourceId = myId, 
    Payload = "{}" 
});
```

### 2. `RelayClientPublisher` (Cloud Relay Hub)
Used for internet play across different networks/NATs. Connects outbound over WebSockets to a cloud `RelayHub` using room codes and session tokens issued by a room management REST API.

```csharp
using Microsoft.Extensions.Logging;
using Sanet.Transport;
using Sanet.Transport.SignalR.Client.Publishers;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var publisher = new RelayClientPublisher(
    hubUrl: "wss://relay.example.com/relayhub",
    roomCode: "ABC234",
    sessionToken: sessionTokenFromRestApi,
    logger: loggerFactory.CreateLogger<RelayClientPublisher>());

publisher.Subscribe(message => Console.WriteLine($"Received: {message.MessageType}"));

await publisher.StartAsync();

await publisher.PublishMessage(new TransportMessage 
{ 
    MessageType = "GameCommand", 
    SourceId = myId, 
    Payload = "{}" 
});
```

## Project Structure

- **Sanet.Transport**: Core interfaces (`ITransportPublisher`) and message definitions (`TransportMessage`)
- **Sanet.Transport.Rx**: Implementation using Reactive Extensions
- **Sanet.Transport.Channel**: Implementation using System.Threading.Channels
- **Sanet.Transport.SignalR.Client**: Client-side SignalR publishers (`SignalRClientPublisher`, `RelayClientPublisher`) and UDP discovery
- **Sanet.Transport.SignalR.Server**: Server-side embedded host (`SignalRHostManager`) and server publisher
- **Sanet.Transport.SignalR.Hub**: Cloud relay room-management web service (SignalR hub + REST API)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
