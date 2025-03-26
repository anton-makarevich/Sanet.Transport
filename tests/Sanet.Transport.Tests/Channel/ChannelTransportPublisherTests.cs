using Sanet.Transport;
using Sanet.Transport.Channel;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Sanet.Transport.Tests.Channel;

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
            CommandType = "TestCommand",
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
            CommandType = "TestCommand",
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
                CommandType = $"TestCommand{i}",
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
            CommandType = "TestCommand",
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
            CommandType = "TestCommand2",
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
                CommandType = $"TestCommand{i}",
                SourceId = Guid.NewGuid(),
                Payload = $"{{\"index\": {i}}}",
                Timestamp = DateTime.UtcNow
            });
        }
        
        // Assert - all messages should be processed
        await Task.Delay(200);
        receivedCount.ShouldBe(messageCount);
    }
}
