using Shouldly;
using Xunit;

namespace Sanet.Transport.Channel.Tests;

public class ChannelTransportPublisherTests
{
    [Fact]
    public async Task Subscribe_WhenMessagePublished_SubscriberReceivesMessage()
    {
        // Arrange
        using var publisher = new ChannelTransportPublisher();
        var receivedMessage = false;
        var testMessage = new TransportMessage
        {
            MessageType = "TestCommand",
            SourceId = Guid.NewGuid(),
            Payload = "{}",
            Timestamp = DateTime.UtcNow
        };
        
        // Act
        publisher.Subscribe(msg =>
        {
            msg.ShouldBe(testMessage);
            receivedMessage = true;
        });
        
        await publisher.PublishMessage(testMessage);
        
        // Assert - wait a bit for async processing
        await Task.Delay(100);
        receivedMessage.ShouldBeTrue();
    }

    [Fact]
    public async Task PublishMessage_WithMultipleSubscribers_AllSubscribersReceiveMessage()
    {
        // Arrange
        using var publisher = new ChannelTransportPublisher();
        var subscriberCount = 3;
        var receivedCount = 0;
        var testMessage = new TransportMessage
        {
            MessageType = "TestCommand",
            SourceId = Guid.NewGuid(),
            Payload = "{}",
            Timestamp = DateTime.UtcNow
        };
        
        // Act
        for (int i = 0; i < subscriberCount; i++)
        {
            publisher.Subscribe(msg =>
            {
                msg.ShouldBe(testMessage);
                Interlocked.Increment(ref receivedCount);
            });
        }
        
        await publisher.PublishMessage(testMessage);
        
        // Assert - wait a bit for async processing
        await Task.Delay(100);
        receivedCount.ShouldBe(subscriberCount);
    }
    
    [Fact]
    public async Task PublishMessage_WithMultipleMessages_AllMessagesAreReceived()
    {
        // Arrange
        using var publisher = new ChannelTransportPublisher();
        var messageCount = 10;
        var receivedCount = 0;
        var messages = new List<TransportMessage>();
        
        for (int i = 0; i < messageCount; i++)
        {
            messages.Add(new TransportMessage
            {
                MessageType = $"TestCommand{i}",
                SourceId = Guid.NewGuid(),
                Payload = $"{{\"index\": {i}}}",
                Timestamp = DateTime.UtcNow
            });
        }
        
        // Act
        publisher.Subscribe(msg =>
        {
            messages.ShouldContain(msg);
            Interlocked.Increment(ref receivedCount);
        });
        
        foreach (var message in messages)
        {
            await publisher.PublishMessage(message);
        }
        
        // Assert - wait a bit for async processing
        await Task.Delay(200);
        receivedCount.ShouldBe(messageCount);
    }
    
    [Fact]
    public async Task Dispose_StopsProcessingMessages()
    {
        // Arrange
        var publisher = new ChannelTransportPublisher();
        var receivedCount = 0;
        
        publisher.Subscribe(_ => Interlocked.Increment(ref receivedCount));
        
        // Act - publish a message and verify it's received
        await publisher.PublishMessage(new TransportMessage
        {
            MessageType = "TestCommand",
            SourceId = Guid.NewGuid(),
            Payload = "{}",
            Timestamp = DateTime.UtcNow
        });
        
        await Task.Delay(100);
        receivedCount.ShouldBe(1);
        
        // Dispose the publisher
        publisher.Dispose();
        
        // Publish another message after disposal
        await publisher.PublishMessage(new TransportMessage
        {
            MessageType = "TestCommand2",
            SourceId = Guid.NewGuid(),
            Payload = "{}",
            Timestamp = DateTime.UtcNow
        });
        
        // Assert - the second message should not be processed
        await Task.Delay(100);
        receivedCount.ShouldBe(1);
    }
    
    [Fact]
    public async Task PublishMessage_WithCustomCapacity_WorksCorrectly()
    {
        // Arrange
        using var publisher = new ChannelTransportPublisher(capacity: 5);
        var receivedCount = 0;
        var messageCount = 10;
        
        publisher.Subscribe(_ => Interlocked.Increment(ref receivedCount));
        
        // Act - publish messages
        for (int i = 0; i < messageCount; i++)
        {
            await publisher.PublishMessage(new TransportMessage
            {
                MessageType = $"TestCommand{i}",
                SourceId = Guid.NewGuid(),
                Payload = $"{{\"index\": {i}}}",
                Timestamp = DateTime.UtcNow
            });
        }
        
        // Assert - all messages should be processed
        await Task.Delay(200);
        receivedCount.ShouldBe(messageCount);
    }

    [Fact]
    public void Subscribe_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var publisher = new ChannelTransportPublisher();
        publisher.Dispose();

        // Act & Assert
        Should.Throw<ObjectDisposedException>(() =>
        {
            publisher.Subscribe(_ => { });
        });
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        var publisher = new ChannelTransportPublisher();

        // Act
        publisher.Dispose();

        // Assert
        Should.NotThrow(() =>
        {
            publisher.Dispose();
        });
    }

    [Fact]
    public async Task Subscribe_WhenSubscriberThrows_OtherSubscribersStillReceiveMessage()
    {
        // Arrange
        // Use a small delay in the console write to allow time for potential processing issues
        using var publisher = new ChannelTransportPublisher(); 
        var subscriber1Received = false;
        var subscriber3Received = false;
        var testMessage = new TransportMessage
        {
            MessageType = "Test",
            SourceId = Guid.NewGuid()
        };

        // Act
        publisher.Subscribe(_ =>
        {
            subscriber1Received = true;
        });
        
        // Subscriber that throws
        publisher.Subscribe(_ => throw new InvalidOperationException("Test Exception from Subscriber 2"));

        publisher.Subscribe(_ =>
        {
            subscriber3Received = true;
        });

        await publisher.PublishMessage(testMessage);

        // Assert - wait a bit for async processing
        await Task.Delay(100); 
        subscriber1Received.ShouldBeTrue("Subscriber 1 should have received the message before the exception.");
        subscriber3Received.ShouldBeTrue("Subscriber 3 should have received the message despite the exception in Subscriber 2.");

        // Verify subsequent messages are still processed (optional but good)
        var subscriber4Received = false;
        var testMessage2 = new TransportMessage
        {
            MessageType = "Test2",
            SourceId = Guid.NewGuid()
        };
        publisher.Subscribe(_ => subscriber4Received = true); // Add a new subscriber
        await publisher.PublishMessage(testMessage2);
        await Task.Delay(100);
        subscriber4Received.ShouldBeTrue("Subsequent messages should still be processed after a subscriber exception.");
    }
}
