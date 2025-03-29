using Sanet.Transport;
using Sanet.Transport.SignalR;
using Sanet.Transport.SignalR.Infrastructure;
using Sanet.Transport.SignalR.Publishers;

Console.WriteLine("Run as (S)erver or (C)lient?");
var mode = Console.ReadKey().KeyChar;
Console.WriteLine();

if (mode == 's' || mode == 'S')
{
    await RunServerAsync();
}
else if (mode == 'c' || mode == 'C')
{
    await RunClientAsync();
}
else
{
    Console.WriteLine("Invalid mode selected.");
}

static async Task RunServerAsync()
{
    Console.WriteLine("Starting server...");
    SignalRHostManager? hostManager = null;
    try
    {
        hostManager = await SignalRTransportFactory.CreateHost();
        Console.WriteLine($"Server started at {hostManager.HubUrl}");
        Console.WriteLine("Waiting for clients...");

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
        hostManager?.Dispose();
    }
}

static async Task RunClientAsync()
{
    SignalRClientPublisher? client = null;
    try
    {
        Console.WriteLine("Enter server URL (leave empty to discover): ");
        var urlInput = Console.ReadLine();
        string? hubUrl;

        if (string.IsNullOrWhiteSpace(urlInput))
        {
            Console.WriteLine("Discovering servers...");
            var hosts = await SignalRTransportFactory.DiscoverHosts(); // Discover for 5 seconds
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

        Console.WriteLine($"Connecting to {hubUrl}...");
        client = SignalRTransportFactory.CreateClient(hubUrl);
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
    Console.WriteLine($"[{sender}]: {message.Payload}");
}

static async Task RunChatLoopAsync(ITransportPublisher publisher)
{
    Console.WriteLine("Enter your message (or type 'exit' to quit):");
    while (Console.ReadLine() is { } input && !input.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(input)) continue;

        var message = new TransportMessage
        {
            MessageType = "ChatMessage",
            // Assign a consistent SourceId if this were a real client app, 
            // but for this simple console app, a new Guid is okay for demo.
            // Server mode will broadcast, Client mode sends directly.
            SourceId = (publisher is SignalRClientPublisher) ? Guid.NewGuid() : Guid.Empty, 
            Payload = input,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            await publisher.PublishMessage(message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending message: {ex.Message}");
            // Optional: Add logic to attempt reconnection if needed
            break; // Exit loop on send error
        }
    }
}
