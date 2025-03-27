using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Sanet.Transport.SignalR.Tests;

public class SignalRHostManagerTests
{
    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        // Arrange & Act
        var hostManager = new SignalRHostManager();
        
        // Assert
        Assert.NotNull(hostManager);
    }
    
    [Fact]
    public void Constructor_WithCustomUrl_CreatesValidInstance()
    {
        // Arrange & Act
        var customUrl = "http://localhost:5001";
        var hostManager = new SignalRHostManager(customUrl);
        
        // Assert
        Assert.NotNull(hostManager);
        // We can't easily test the URL was set correctly without exposing it
    }
    
    [Fact]
    public void Publisher_ReturnsNonNullInstance()
    {
        // Arrange
        var hostManager = new SignalRHostManager();
        
        // Act
        var publisher = hostManager.Publisher;
        
        // Assert
        Assert.NotNull(publisher);
        Assert.IsType<SignalRServerPublisher>(publisher);
    }
    
    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Arrange
        var hostManager = new SignalRHostManager();
        
        // Act & Assert - should not throw
        hostManager.Dispose();
    }
}
