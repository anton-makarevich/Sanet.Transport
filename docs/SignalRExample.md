# Using SignalR Transport in Avalonia Apps

This guide demonstrates how to use the SignalR transport implementation in Avalonia applications without directly adding ASP.NET Core dependencies to your apps.

## Overview

The `Sanet.Transport.SignalR` library provides a complete SignalR implementation of the `ITransportPublisher` interface with all the ASP.NET Core infrastructure encapsulated within the library. This allows Avalonia applications to communicate over a LAN without having to include web server dependencies directly.

## Features

- Self-contained SignalR host that can be embedded in any application
- Automatic network discovery for finding hosts on the LAN
- Clean API for both host and client applications
- No direct ASP.NET Core dependencies in your Avalonia apps

## Host Application Example

Here's how to create a host application that other clients can connect to:

```csharp
using Sanet.Transport;
using Sanet.Transport.SignalR;

namespace AvaloniaHostApp
{
    public class MainViewModel
    {
        private SignalRHostManager _hostManager;
        private ITransportPublisher _publisher;

        public async Task InitializeHost()
        {
            // Create and start a SignalR host (defaults to http://0.0.0.0:5000)
            _hostManager = await SignalRTransportFactory.CreateHostAsync();
            
            // Get the publisher to send/receive messages
            _publisher = _hostManager.Publisher;
            
            // Subscribe to incoming messages
            _publisher.Subscribe(HandleIncomingMessage);
        }
        
        private void HandleIncomingMessage(TransportMessage message)
        {
            Console.WriteLine($"Received message: {message.MessageType} from {message.SourceId}");
            Console.WriteLine($"Payload: {message.Payload}");
            
            // Process the message...
        }
        
        public async Task SendMessage(string messageType, string payload)
        {
            var message = new TransportMessage
            {
                MessageType = messageType,
                SourceId = Guid.NewGuid(), // Use a consistent ID for your app
                Payload = payload,
                Timestamp = DateTime.UtcNow
            };
            
            await _publisher.PublishMessage(message);
        }
        
        public void Cleanup()
        {
            _hostManager?.Dispose();
        }
    }
}
```

## Client Application Example

Here's how to create a client application that connects to a host:

```csharp
using Sanet.Transport;
using Sanet.Transport.SignalR;

namespace AvaloniaClientApp
{
    public class MainViewModel
    {
        private SignalRClientPublisher _client;
        
        public async Task<bool> ConnectToHost()
        {
            // Option 1: Discover hosts automatically
            var hosts = await SignalRTransportFactory.DiscoverHostsAsync();
            if (hosts.Count == 0)
            {
                Console.WriteLine("No hosts found on the network");
                return false;
            }
            
            // Connect to the first host found
            return await ConnectToSpecificHost(hosts[0]);
        }
        
        public async Task<bool> ConnectToSpecificHost(string hubUrl)
        {
            try
            {
                // Create a client publisher
                _client = SignalRTransportFactory.CreateClient(hubUrl);
                
                // Subscribe to incoming messages
                _client.Subscribe(HandleIncomingMessage);
                
                // Start the connection
                await _client.StartAsync();
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to host: {ex.Message}");
                return false;
            }
        }
        
        private void HandleIncomingMessage(TransportMessage message)
        {
            Console.WriteLine($"Received message: {message.MessageType} from {message.SourceId}");
            Console.WriteLine($"Payload: {message.Payload}");
            
            // Process the message...
        }
        
        public async Task SendMessage(string messageType, string payload)
        {
            if (_client == null)
            {
                throw new InvalidOperationException("Not connected to a host");
            }
            
            var message = new TransportMessage
            {
                MessageType = messageType,
                SourceId = Guid.NewGuid(), // Use a consistent ID for your app
                Payload = payload,
                Timestamp = DateTime.UtcNow
            };
            
            await _client.PublishMessage(message);
        }
        
        public async Task Cleanup()
        {
            if (_client != null)
            {
                await _client.DisposeAsync();
            }
        }
    }
}
```

## Benefits

1. **Clean Architecture**: Your Avalonia apps don't need to reference ASP.NET Core directly
2. **Simple API**: The factory pattern makes it easy to create hosts and clients
3. **Network Discovery**: Clients can automatically find hosts on the LAN
4. **Consistent Interface**: Uses the same `ITransportPublisher` interface as other transport implementations

## Notes

- The host will be accessible at `http://[ip]:5000/transporthub` by default
- You can customize the host URL by passing a different URL to `CreateHostAsync()`
- Discovery uses UDP broadcasts on port 5001
- For security in production environments, consider adding authentication
