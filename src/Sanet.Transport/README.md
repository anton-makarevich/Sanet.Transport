# Sanet.Transport

Core abstractions for message-based communication in distributed systems.

[![NuGet](https://img.shields.io/nuget/v/Sanet.Transport?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport/)

## Overview

Sanet.Transport provides the core interfaces and message definitions for a simple, extensible architecture for publishing and subscribing to messages between different parts of an application or between distributed systems.

## Key Components

- **ITransportPublisher**: Core interface for publishing and subscribing to messages
- **TransportConnectionState**: Describes the connectivity state of a transport connection
- **IPublisherFactory**: Creates transport publishers from transport-specific options
- **PublisherOptions**: Marker base type for transport-specific publisher options
- **TransportMessage**: Standard message format for all transport implementations

## Connection State

Every `ITransportPublisher` exposes:

- `TransportConnectionState ConnectionState` - the current connectivity state
- `event Action<TransportConnectionState>? ConnectionStateChanged` - raised on every connection-state transition

The states are:

- `Connecting` - the connection is being established
- `Connected` - the connection is established and operational
- `Reconnecting` - the connection was lost and is being re-established (non-terminal)
- `Disconnected` - the connection is not active (non-terminal)
- `Closed` - the connection has been closed (**terminal**, a new publisher must be created)

The event reports transport connectivity only — it is **not** raised when peers or hosts join or leave a room. Implementations report these states differently; see the package-specific READMEs for the exact mapping.

```csharp
publisher.ConnectionStateChanged += state =>
{
    switch (state)
    {
        case TransportConnectionState.Closed:
            // Terminal - recreate the publisher before continuing
            break;
        case TransportConnectionState.Disconnected:
        case TransportConnectionState.Reconnecting:
            // Transient - disable sending, show a reconnecting indicator
            break;
        case TransportConnectionState.Connected:
            // Re-enable sending
            break;
    }
};
```

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
