using Sanet.Transport;
using Sanet.Transport.SignalR;
using Sanet.Transport.SignalR.Infrastructure;
using Sanet.Transport.SignalR.Publishers;
using Sanet.Transport.SignalR.Discovery;

Console.WriteLine("Run as (S)erver or (C)lient?");
var mode = Console.ReadKey().KeyChar;
Console.WriteLine();

switch (mode)
{
    case 's' or 'S':
        await RunServerAsync();
        break;
    case 'c' or 'C':
        await RunClientAsync();
        break;
    default:
        Console.WriteLine("Invalid mode selected.");
        break;
}

return;

static async Task RunServerAsync()
{
    Console.WriteLine("Starting server...");
    SignalRHostManager? hostManager = null;
    IDiscoveryService? discoveryService = null;
    try
    {
        // Create the host manager with default port 5000
        hostManager = new SignalRHostManager();
        
        // Start the host
        await hostManager.Start();
        Console.WriteLine($"Server started at {hostManager.HubUrl}");

        // Start Discovery Broadcasting
        discoveryService = new MulticastDiscoveryService();
        discoveryService.BroadcastPresence(hostManager.HubUrl);
        Console.WriteLine("Broadcasting presence on the network...");

        Console.WriteLine("Waiting for clients...");

        // Subscribe to messages
        var publisher = hostManager.Publisher;
        publisher.Subscribe(HandleIncomingMessage);

        await RunChatLoopAsync(publisher);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Server error: {ex.Message}");
    }
    finally
    {
        Console.WriteLine("Shutting down server...");
        discoveryService?.Dispose(); // Stop broadcasting and release resources
        hostManager?.Dispose(); // Stop the web host
    }
}

static async Task RunClientAsync()
{
    SignalRClientPublisher? client = null;
    IDiscoveryService? discoveryService = null;
    try
    {
        Console.WriteLine("Enter server URL (leave empty to discover): ");
        var urlInput = Console.ReadLine();
        string? hubUrl;

        if (string.IsNullOrWhiteSpace(urlInput))
        {
            Console.WriteLine("Discovering servers...");
            discoveryService = new MulticastDiscoveryService();
            var hosts = await discoveryService.DiscoverHosts(timeoutSeconds: 5); // Discover for 5 seconds
            discoveryService.Dispose(); // Dispose after discovery is done
            discoveryService = null;

            if (hosts.Count == 0)
            {
                Console.WriteLine("No servers found on the network.");
                return;
            }
            hubUrl = hosts[0]; // Connect to the first one found
            Console.WriteLine($"Found server: {hubUrl}");
        }
        else
        {
            hubUrl = urlInput;
        }

        if (string.IsNullOrEmpty(hubUrl))
        {
            Console.WriteLine("No server URL specified or discovered.");
            return;
        }

        Console.WriteLine($"Connecting to {hubUrl}...");
        // Create client publisher
        client = new SignalRClientPublisher(hubUrl);
        client.Subscribe(HandleIncomingMessage);

        await client.StartAsync();
        Console.WriteLine("Connected! You can now send messages.");

        await RunChatLoopAsync(client);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Client error: {ex.Message}");
    }
    finally
    {
        Console.WriteLine("Disconnecting...");
        discoveryService?.Dispose(); // Ensure disposal if discovery was interrupted
        if (client != null)
        {
            await client.DisposeAsync();
        }
    }
}

static void HandleIncomingMessage(TransportMessage message)
{
    // Simple differentiation: if SourceId is null or empty Guid, assume it's from the server itself (e.g., a broadcast)
    // Otherwise, it's from another client.
    var sender = message.SourceId == Guid.Empty ? "Server" : $"Client_{message.SourceId.ToString()[..4]}";
    var originalColor = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Cyan; 
    Console.WriteLine($"\n[{sender}]: {message.Payload}");
    Console.ForegroundColor = originalColor;
    Console.Write("Enter your message (or type 'exit' to quit): "); // Re-prompt user
}

static async Task RunChatLoopAsync(ITransportPublisher publisher)
{
    Console.Write("Enter your message (or type 'exit' to quit): "); // Use Write for initial prompt
    while (Console.ReadLine() is { } input && !input.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(input)) 
        {
             Console.Write("Enter your message (or type 'exit' to quit): "); // Re-prompt if empty
            continue;
        }

        var message = new TransportMessage
        {
            MessageType = "ChatMessage",
            // Assign a consistent SourceId if this were a real client app, 
            // but for this simple console app, a new Guid is okay for demo.
            // Server mode will broadcast with Empty Guid, Client mode sends with a new Guid.
            SourceId = (publisher is SignalRClientPublisher) ? Guid.NewGuid() : Guid.Empty, 
            Payload = input,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            await publisher.PublishMessage(message);
            Console.Write("Enter your message (or type 'exit' to quit): "); // Re-prompt after sending
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError sending message: {ex.Message}");
            // Optional: Add logic to attempt reconnection if needed
            break; // Exit loop on send error
        }
    }
}
