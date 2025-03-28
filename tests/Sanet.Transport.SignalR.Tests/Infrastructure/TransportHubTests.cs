using Sanet.Transport.SignalR.Infrastructure;
using Shouldly;
using Xunit;

namespace Sanet.Transport.SignalR.Tests.Infrastructure;

public class TransportHubTests
{
    [Fact]
    public void ReceiveMessage_RaisesMessageReceivedEvent()
    {
        // Arrange
        var message = new TransportMessage
        {
            MessageType = "TestMessage",
            SourceId = Guid.NewGuid(),
            Payload = "TestPayload"
        };
        
        TransportMessage? receivedMessage = null;
        TransportHub.MessageReceived += (msg) => receivedMessage = msg;
        
        // Act
        TransportHub.SimulateMessageReceived(message);
        
        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.MessageType.ShouldBe(message.MessageType);
        receivedMessage.SourceId.ShouldBe(message.SourceId);
        receivedMessage.Payload.ShouldBe(message.Payload);
    }
    
    [Fact]
    public void ReceiveMessage_WithNoSubscribers_DoesNotThrow()
    {
        // Arrange
        var message = new TransportMessage
        {
            MessageType = "TestMessage",
            SourceId = Guid.NewGuid(),
            Payload = "TestPayload"
        };
        
        // Clear any existing subscribers
        //TransportHub.ClearSubscribers();
        
        // Act & Assert - should not throw
        TransportHub.SimulateMessageReceived(message);
    }
}
