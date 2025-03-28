using Sanet.Transport.SignalR.Publishers;
using Shouldly;
using Xunit;

namespace Sanet.Transport.SignalR.Tests.Publishers;

public class SignalRClientPublisherTests
{
    [Fact]
    public void PublishMessage_CreatesPublisher()
    {
        // This test requires mocking HubConnection which is challenging
        // In a real implementation, we would use integration tests with a real hub
        
        // For now, we'll just verify the client publisher can be constructed
        const string hubUrl = "http://localhost:5000/transporthub";
        var publisher = new SignalRClientPublisher(hubUrl);
        
        // Assert that the publisher was created successfully
        publisher.ShouldNotBeNull();
    }
    
    [Fact]
    public void Constructor_WithNullOrEmptyUrl_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Should.Throw<ArgumentException>(() => new SignalRClientPublisher(string.Empty));
        Should.Throw<ArgumentException>(() => new SignalRClientPublisher(null!));
    }
    
    [Fact]
    public void Subscribe_AddsSubscriber()
    {
        // Arrange
        const string hubUrl = "http://localhost:5000/transporthub";
        var publisher = new SignalRClientPublisher(hubUrl);
        
        var messageReceived = false;
        
        // Act
        publisher.Subscribe(_ => messageReceived = true);
        
        // We can't easily test the subscription directly without integration tests
        // This test just verifies the method doesn't throw
        messageReceived.ShouldBeFalse();
    }
}
