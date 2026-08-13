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
        var publishReturned = publishTask.IsCompleted;
        subscriberCanFinish.SetResult();

        publishReturned.ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeAsync_DisposesPublisher_SubscriberIsNoLongerNotified()
    {
        // Arrange
        var publisher = new RxTransportPublisher(ImmediateScheduler.Instance);
        var receivedCount = 0;
        publisher.Subscribe(_ => Interlocked.Increment(ref receivedCount));

        var testMessage = new TransportMessage
        {
            MessageType = "TestCommand",
            SourceId = Guid.NewGuid(),
            Payload = "{}",
            Timestamp = DateTime.UtcNow
        };

        // Act
        await publisher.DisposeAsync();
        await publisher.PublishMessage(testMessage);

        // Assert
        receivedCount.ShouldBe(0);
    }

    [Fact]
    public async Task DisposeAsync_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        var publisher = new RxTransportPublisher();

        // Act & Assert
        await publisher.DisposeAsync();
        Should.NotThrow(async () => await publisher.DisposeAsync());
    }

    [Fact]
    public async Task Subscribe_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var publisher = new RxTransportPublisher();
        await publisher.DisposeAsync();

        // Act & Assert
        Should.Throw<ObjectDisposedException>(() => publisher.Subscribe(_ => { }));
    }
}
