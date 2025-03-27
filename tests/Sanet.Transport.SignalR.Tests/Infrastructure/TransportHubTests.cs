using System;
using Xunit;

namespace Sanet.Transport.SignalR.Tests;

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
        Assert.NotNull(receivedMessage);
        Assert.Equal(message.MessageType, receivedMessage.MessageType);
        Assert.Equal(message.SourceId, receivedMessage.SourceId);
        Assert.Equal(message.Payload, receivedMessage.Payload);
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
