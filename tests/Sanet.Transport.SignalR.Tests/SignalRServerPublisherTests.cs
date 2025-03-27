using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Sanet.Transport.SignalR.Tests;

public class SignalRServerPublisherTests
{
    private readonly IHubContext<TransportHub> _mockHubContext;
    private readonly IHubClients _mockHubClients;
    private readonly IClientProxy _mockClientProxy;
    private readonly SignalRServerPublisher _publisher;

    public SignalRServerPublisherTests()
    {
        _mockHubContext = Substitute.For<IHubContext<TransportHub>>();
        _mockHubClients = Substitute.For<IHubClients>();
        _mockClientProxy = Substitute.For<IClientProxy>();

        _mockHubContext.Clients.Returns(_mockHubClients);
        _mockHubClients.All.Returns(_mockClientProxy);

        _publisher = new SignalRServerPublisher(_mockHubContext);
    }

    [Fact]
    public async Task PublishMessage_SendsMessageToAllClients()
    {
        // Arrange
        var message = new TransportMessage
        {
            MessageType = "TestMessage",
            SourceId = Guid.NewGuid(),
            Payload = "TestPayload"
        };

        // Act
        await _publisher.PublishMessage(message);

        // Assert
        await _mockClientProxy.Received(1).SendAsync("ReceiveMessage", Arg.Is<TransportMessage>(m => 
            m.MessageType == message.MessageType && 
            m.SourceId == message.SourceId && 
            m.Payload == message.Payload));
    }

    [Fact]
    public void Subscribe_NotifiesSubscriberWhenMessageReceived()
    {
        // Arrange
        var message = new TransportMessage
        {
            MessageType = "TestMessage",
            SourceId = Guid.NewGuid(),
            Payload = "TestPayload"
        };

        TransportMessage? receivedMessage = null;
        _publisher.Subscribe(m => receivedMessage = m);

        // Act
        TransportHub.SimulateMessageReceived(message);

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(message.MessageType, receivedMessage.MessageType);
        Assert.Equal(message.SourceId, receivedMessage.SourceId);
        Assert.Equal(message.Payload, receivedMessage.Payload);
    }

    [Fact]
    public void Dispose_UnsubscribesFromHubEvent()
    {
        // Arrange
        var message = new TransportMessage
        {
            MessageType = "TestMessage",
            SourceId = Guid.NewGuid(),
            Payload = "TestPayload"
        };

        bool messageReceived = false;
        _publisher.Subscribe(_ => messageReceived = true);

        // Act
        _publisher.Dispose();
        TransportHub.SimulateMessageReceived(message);

        // Assert
        Assert.False(messageReceived);
    }
}
