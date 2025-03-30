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

For specific implementations, install the corresponding package:
```
dotnet add package Sanet.Transport.Rx
dotnet add package Sanet.Transport.Channel
dotnet add package Sanet.Transport.SignalR
```

## Library Documentation

For detailed documentation on each implementation, please refer to the README files in the respective library directories:

- [Sanet.Transport](src/Sanet.Transport/README.md) - Core abstractions
- [Sanet.Transport.Rx](src/Sanet.Transport.Rx/README.md) - Reactive Extensions implementation
- [Sanet.Transport.Channel](src/Sanet.Transport.Channel/README.md) - System.Threading.Channels implementation
- [Sanet.Transport.SignalR](src/Sanet.Transport.SignalR/README.md) - ASP.NET Core SignalR implementation

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
