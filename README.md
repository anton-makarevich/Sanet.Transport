# Sanet.Transport

A lightweight, flexible transport layer for message passing between distributed systems.

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

Add the required projects to your solution:

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
- **Sanet.Transport.Tests**: Unit tests for all implementations

## License

This project is licensed under the MIT License - see the LICENSE file for details.
