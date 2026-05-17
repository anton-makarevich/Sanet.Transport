using System.Reactive.Concurrency;
using Shouldly;
using Xunit;

namespace Sanet.Transport.Rx.Tests;

public class RxTransportPublisherTests
{
    [Fact]
    public async Task Subscribe_WhenMessagePublished_SubscriberReceivesMessage()
    {
        // Arrange
        var publisher = new RxTransportPublisher(ImmediateScheduler.Instance);
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
        
        // Assert
        receivedMessage.ShouldBeTrue();
    }

    [Fact]
    public async Task PublishMessage_WithMultipleSubscribers_AllSubscribersReceiveMessage()
    {
        // Arrange
        var publisher = new RxTransportPublisher(ImmediateScheduler.Instance);
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
                receivedCount++;
            });
        }
        
        await publisher.PublishMessage(testMessage);
        
        // Assert
        receivedCount.ShouldBe(subscriberCount);
    }

    [Fact]
    public async Task PublishMessage_DoesNotBlockCaller_WhenSubscriberIsSlow()
    {
        var publisher = new RxTransportPublisher();
        var subscriberStarted = new TaskCompletionSource();
        var subscriberCanFinish = new TaskCompletionSource();
        var publishReturned = false;

        publisher.Subscribe(async _ =>
        {
            subscriberStarted.SetResult();
            await subscriberCanFinish.Task;
        });

        var publishTask = publisher.PublishMessage(new TransportMessage
        {
            MessageType = "Test",
            SourceId = Guid.NewGuid(),
            Payload = "{}",
            Timestamp = DateTime.UtcNow
        });

        await subscriberStarted.Task;
        publishReturned = publishTask.IsCompleted;
        subscriberCanFinish.SetResult();

        publishReturned.ShouldBeTrue();
    }
}
