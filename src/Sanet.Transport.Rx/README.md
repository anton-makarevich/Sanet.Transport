# Sanet.Transport.Rx

Reactive Extensions implementation of the Sanet.Transport abstractions.

[![NuGet](https://img.shields.io/nuget/v/Sanet.Transport.Rx?logo=nuget)](https://www.nuget.org/packages/Sanet.Transport.Rx/)

## Overview

Sanet.Transport.Rx provides an implementation of the `ITransportPublisher` interface using Reactive Extensions (Rx.NET), enabling reactive programming patterns for message passing.

## Features

- Reactive programming model with Rx.NET
- Efficient message handling with observable streams
- Compatible with all Rx operators for advanced message processing

## Installation

```
dotnet add package Sanet.Transport.Rx
```

Or via the Package Manager Console:
```
Install-Package Sanet.Transport.Rx
```

## Usage

```csharp
// Create a Rx transport publisher
ITransportPublisher publisher = new RxTransportPublisher();

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
