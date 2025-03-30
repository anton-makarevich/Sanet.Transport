# SignalR Chat Sample Application

This sample demonstrates how to use the `Sanet.Transport.SignalR` library to create a simple chat application with server and client modes.

## Features

- Run as a server (host) that other clients can connect to
- Run as a client that can connect to a server
- Automatic server discovery on the local network
- Simple text chat between all connected clients

## How to Run

### Build the Project

```bash
dotnet build
```

### Run as a Server

```bash
dotnet run
```

When prompted, press `S` to run in server mode. The server will start and display its URL.

### Run as a Client

In a different terminal:

```bash
dotnet run
```

When prompted, press `C` to run in client mode.

- You can leave the server URL empty to auto-discover servers on your network
- Or you can enter a specific server URL (e.g., `http://localhost:5000/transporthub`)

## How It Works

This sample demonstrates the core functionality of the `Sanet.Transport.SignalR` library:

1. **Server Mode**:
   - Creates a self-contained SignalR host using `SignalRHostManager`
   - Broadcasts its presence on the network using UDP
   - Handles incoming messages and broadcasts them to all clients

2. **Client Mode**:
   - Discovers servers on the network or connects to a specified URL
   - Establishes a SignalR connection to the server
   - Sends and receives messages through the SignalR hub

## Implementation Details

- Shows manual setup of `SignalRHostManager` (Server) and `SignalRClientPublisher` (Client)
- Demonstrates network discovery using `MulticastDiscoveryService` and `DiscoverHosts()`
- Shows how to subscribe to and publish messages using `ITransportPublisher`
- Handles connection, disposal, and basic error scenarios

## Notes

- The server binds to all network interfaces (0.0.0.0) but automatically advertises itself using a routable IP address
- The `SignalRHostManager` resolves the machine's actual IP address when providing the hub URL
- This ensures clients can connect to the server without manual URL adjustments
