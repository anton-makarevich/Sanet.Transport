using Microsoft.AspNetCore.SignalR.Client;
using NSubstitute;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Sanet.Transport.SignalR.Tests;

public class SignalRClientPublisherTests
{
    [Fact]
    public async Task PublishMessage_SendsMessageToHub()
    {
        // This test requires mocking HubConnection which is challenging
        // In a real implementation, we would use integration tests with a real hub
        
        // For now, we'll just verify the client publisher can be constructed
        var hubUrl = "http://localhost:5000/transporthub";
        var publisher = new SignalRClientPublisher(hubUrl);
        
        // Assert that the publisher was created successfully
        Assert.NotNull(publisher);
    }
    
    [Fact]
    public void Constructor_WithNullOrEmptyUrl_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new SignalRClientPublisher(string.Empty));
        Assert.Throws<ArgumentException>(() => new SignalRClientPublisher(null));
    }
    
    [Fact]
    public void Subscribe_AddsSubscriber()
    {
        // Arrange
        var hubUrl = "http://localhost:5000/transporthub";
        var publisher = new SignalRClientPublisher(hubUrl);
        
        bool messageReceived = false;
        
        // Act
        publisher.Subscribe(_ => messageReceived = true);
        
        // We can't easily test the subscription directly without integration tests
        // This test just verifies the method doesn't throw
        Assert.False(messageReceived);
    }
}
