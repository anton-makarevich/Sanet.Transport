using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Sanet.Transport.SignalR.Tests.Publishers;

public class SignalRServerPublisherTests
{
    [Fact]
    public async Task PublishMessage_SendsMessageToAllClients()
    {
        // Arrange
        var clientProxy = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        clients.All.Returns(clientProxy);
        
        var hubContext = Substitute.For<IHubContext<TransportHub>>();
        hubContext.Clients.Returns(clients);
        
        var publisher = new SignalRServerPublisher(hubContext);
        
        var testMessage = new TransportMessage
        {
            MessageType = "TestCommand",
            SourceId = Guid.NewGuid(),
            Payload = "{}",
            Timestamp = DateTime.UtcNow
        };
        
        // Act
        await publisher.PublishMessage(testMessage);
        
        // Assert
        await clientProxy.Received(1).SendCoreAsync(
            "ReceiveMessage", 
            Arg.Is<object[]>(args => args.Length == 1 && args[0] is TransportMessage), 
            Arg.Any<CancellationToken>());
    }
    
    [Fact]
    public void Subscribe_WhenMessageReceived_SubscriberIsNotified()
    {
        // Arrange
        var hubContext = Substitute.For<IHubContext<TransportHub>>();
        var publisher = new SignalRServerPublisher(hubContext);
        
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
            msg.MessageType.ShouldBe(testMessage.MessageType);
            msg.SourceId.ShouldBe(testMessage.SourceId);
            msg.Payload.ShouldBe(testMessage.Payload);
            receivedMessage = true;
        });
        
        // Simulate a message received from the hub
        TransportHub.SimulateMessageReceived(testMessage);
        
        // Assert
        receivedMessage.ShouldBeTrue();
    }
    
    [Fact]
    public void PublishMessage_WithMultipleSubscribers_AllSubscribersReceiveMessage()
    {
        // Arrange
        var hubContext = Substitute.For<IHubContext<TransportHub>>();
        var publisher = new SignalRServerPublisher(hubContext);
        
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
        for (var i = 0; i < subscriberCount; i++)
        {
            publisher.Subscribe(msg =>
            {
                msg.MessageType.ShouldBe(testMessage.MessageType);
                msg.SourceId.ShouldBe(testMessage.SourceId);
                msg.Payload.ShouldBe(testMessage.Payload);
                Interlocked.Increment(ref receivedCount);
            });
        }
        
        // Simulate a message received from the hub
        TransportHub.SimulateMessageReceived(testMessage);
        
        // Assert
        receivedCount.ShouldBe(subscriberCount);
    }
    
    [Fact]
    public void Dispose_UnsubscribesFromHubEvents()
    {
        // Arrange
        var hubContext = Substitute.For<IHubContext<TransportHub>>();
        var publisher = new SignalRServerPublisher(hubContext);
        
        var receivedMessage = false;
        publisher.Subscribe(_ => receivedMessage = true);
        
        // Act
        publisher.Dispose();
        
        // Simulate a message received from the hub
        TransportHub.SimulateMessageReceived(new TransportMessage
        {
            MessageType = "TestCommand",
            SourceId = Guid.NewGuid(),
            Payload = "{}",
            Timestamp = DateTime.UtcNow
        });
        
        // Assert - the subscriber should not be notified after disposal
        receivedMessage.ShouldBeFalse();
    }
}