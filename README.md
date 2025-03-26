# Sanet.Transport

A lightweight transport layer for message passing between distributed systems.

[![Build Status](https://github.com/anton-makarevich/Sanet.Transport/actions/workflows/transport.yml/badge.svg)](https://github.com/anton-makarevich/Sanet.Transport/actions/workflows/transport.yml)
[![codecov](https://codecov.io/gh/anton-makarevich/Sanet.Transport/branch/main/graph/badge.svg)](https://codecov.io/gh/anton-makarevich/Sanet.Transport)

| Package | Version |
| ------- | ------- |
| Sanet.Transport | [![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.svg)](https://www.nuget.org/packages/Sanet.Transport/) |
| Sanet.Transport.Rx | [![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.Rx.svg)](https://www.nuget.org/packages/Sanet.Transport.Rx/) |
| Sanet.Transport.Channel | [![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.Channel.svg)](https://www.nuget.org/packages/Sanet.Transport.Channel/) |

## Overview

Sanet.Transport provides a simple, extensible architecture for publishing and subscribing to messages between different parts of an application or between distributed systems. It's designed to be independent of any specific game or application logic, making it reusable across different projects.

## Key Features

- **Decoupled Communication**: Enables communication between components without direct dependencies
- **Multiple Transport Implementations**:
  - **Rx**: Using Reactive Extensions for reactive programming patterns
  - **Channel**: Using System.Threading.Channels for high-performance message passing
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

Or via the Package Manager Console:
```
Install-Package Sanet.Transport
Install-Package Sanet.Transport.Rx
Install-Package Sanet.Transport.Channel
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

## Project Structure

- **Sanet.Transport**: Core interfaces and message definitions
- **Sanet.Transport.Rx**: Implementation using Reactive Extensions
- **Sanet.Transport.Channel**: Implementation using System.Threading.Channels
- **Sanet.Transport.Rx.Tests**: Unit tests for Rx implementation
- **Sanet.Transport.Channel.Tests**: Unit tests for Channel implementation

## License

This project is licensed under the MIT License - see the LICENSE file for details.
