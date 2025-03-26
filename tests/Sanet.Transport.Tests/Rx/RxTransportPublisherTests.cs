using Sanet.Transport;
using Sanet.Transport.Rx;
using Shouldly;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Sanet.Transport.Tests.Rx;

public class RxTransportPublisherTests
{
    [Fact]
    public async Task Subscribe_WhenMessagePublished_SubscriberReceivesMessage()
    {
        // Arrange
        var publisher = new RxTransportPublisher();
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
        
        // Assert
        receivedMessage.ShouldBeTrue();
    }

    [Fact]
    public async Task PublishMessage_WithMultipleSubscribers_AllSubscribersReceiveMessage()
    {
        // Arrange
        var publisher = new RxTransportPublisher();
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
                receivedCount++;
            });
        }
        
        await publisher.PublishMessage(testMessage);
        
        // Assert
        receivedCount.ShouldBe(subscriberCount);
    }
}
