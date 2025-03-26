using System.Threading.Channels;

namespace Sanet.Transport.Channel;

/// <summary>
/// Implementation of ITransportPublisher using System.Threading.Channels
/// </summary>
public class ChannelTransportPublisher : ITransportPublisher, IDisposable
{
    private readonly Channel<TransportMessage> _channel;
    private readonly List<Action<TransportMessage>> _subscribers = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;
    private bool _isDisposed;
    
    /// <summary>
    /// Creates a new instance of ChannelTransportPublisher
    /// </summary>
    /// <param name="capacity">The maximum capacity of the channel (default: 100)</param>
    public ChannelTransportPublisher(int capacity = 100)
    {
        // Create a bounded channel for message processing
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        
        _channel = System.Threading.Channels.Channel.CreateBounded<TransportMessage>(options);
        
        // Start the background processing task
        _processingTask = Task.Run(ProcessMessagesAsync);
    }
    
    /// <summary>
    /// Publishes a transport message to all subscribers
    /// </summary>
    /// <param name="message">The message to publish</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task PublishMessage(TransportMessage message)
    {
        // If the publisher has been disposed, return a completed task
        if (_isDisposed)
        {
            return;
        }
        
        try
        {
            await _channel.Writer.WriteAsync(message, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Channel has been closed or cancellation requested
        }
        catch (ObjectDisposedException)
        {
            // CancellationTokenSource has been disposed
        }
    }
    
    /// <summary>
    /// Subscribes to receive transport messages
    /// </summary>
    /// <param name="onMessageReceived">Action to call when a message is received</param>
    public void Subscribe(Action<TransportMessage> onMessageReceived)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ChannelTransportPublisher));
        }
        
        lock (_subscribers)
        {
            _subscribers.Add(onMessageReceived);
        }
    }
    
    private async Task ProcessMessagesAsync()
    {
        try
        {
            // Process messages until cancellation is requested
            await foreach (var message in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                // Get a snapshot of the current subscribers
                Action<TransportMessage>[] currentSubscribers;
                lock (_subscribers)
                {
                    currentSubscribers = _subscribers.ToArray();
                }
                
                // Notify each subscriber
                foreach (var subscriber in currentSubscribers)
                {
                    try
                    {
                        subscriber(message);
                    }
                    catch (Exception ex)
                    {
                        // Log the exception in a real application
                        Console.WriteLine($"Error notifying subscriber: {ex}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            // Log the exception in a real application
            Console.WriteLine($"Error processing messages: {ex}");
        }
    }
    
    /// <summary>
    /// Disposes resources used by the publisher
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }
        
        _isDisposed = true;
        
        // Request cancellation
        _cts.Cancel();
        
        // Complete the channel
        _channel.Writer.Complete();
        
        try
        {
            // Wait for the processing task to complete
            _processingTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception)
        {
            // Ignore exceptions during disposal
        }
        
        // Dispose the cancellation token source
        _cts.Dispose();
        
        GC.SuppressFinalize(this);
    }
}
