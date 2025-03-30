# Sanet.Transport.Channel

System.Threading.Channels implementation of the Sanet.Transport abstractions.

[![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.Channel?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport.Channel/)

## Overview

Sanet.Transport.Channel provides an implementation of the `ITransportPublisher` interface using System.Threading.Channels, enabling high-performance message passing with back-pressure support.

## Features

- High-performance message passing
- Producer-consumer patterns
- Thread-safe communication

## Installation

```
dotnet add package Sanet.Transport.Channel
```

Or via the Package Manager Console:
```
Install-Package Sanet.Transport.Channel
```

## Usage

```csharp
// Create a Channel transport publisher
ITransportPublisher publisher = new ChannelTransportPublisher();

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

## License

This project is licensed under the MIT License - see the LICENSE file for details.
