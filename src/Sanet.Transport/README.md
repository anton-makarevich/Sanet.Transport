# Sanet.Transport

Core abstractions for message-based communication in distributed systems.

[![NuGet](https://img.shields.io/nuget/v/Sanet.Transport?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport/)

## Overview

Sanet.Transport provides the core interfaces and message definitions for a simple, extensible architecture for publishing and subscribing to messages between different parts of an application or between distributed systems.

## Key Components

- **ITransportPublisher**: Core interface for publishing and subscribing to messages
- **TransportMessage**: Standard message format for all transport implementations

## Basic Usage

```csharp
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

## Available Implementations

- [Sanet.Transport.Rx](https://www.nuget.org/packages/Sanet.Transport.Rx/) - Reactive Extensions implementation
- [Sanet.Transport.Channel](https://www.nuget.org/packages/Sanet.Transport.Channel/) - System.Threading.Channels implementation
- [Sanet.Transport.SignalR](https://www.nuget.org/packages/Sanet.Transport.SignalR/) - ASP.NET Core SignalR implementation

## License

This project is licensed under the MIT License - see the LICENSE file for details.
