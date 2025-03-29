using Sanet.Transport.SignalR.Infrastructure;
using Sanet.Transport.SignalR.Publishers;
using Shouldly;
using Xunit;

namespace Sanet.Transport.SignalR.Tests.Infrastructure;

public class SignalRHostManagerTests
{
    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        // Arrange & Act
        var hostManager = new SignalRHostManager();
        
        // Assert
        hostManager.ShouldNotBeNull();
    }
    
    [Fact]
    public void Constructor_WithCustomPort_CreatesValidInstance()
    {
        // Arrange & Act
        const int customPort = 5001;
        var hostManager = new SignalRHostManager(customPort);
        
        // Assert
        hostManager.ShouldNotBeNull();
    }
    
    [Fact]
    public async Task Publisher_ReturnsNonNullInstance()
    {
        // Arrange
        var hostManager = new SignalRHostManager();
        await hostManager.Start();
        
        // Act
        var publisher = hostManager.Publisher;
        
        // Assert
        publisher.ShouldNotBeNull();
        publisher.ShouldBeOfType<SignalRServerPublisher>();
        hostManager.Dispose();
    }
    
    [Fact]
    public async Task Dispose_DoesNotThrow()
    {
        // Arrange
        var hostManager = new SignalRHostManager(4569,"myHub");
        await hostManager.Start();
        
        // Act & Assert - should not throw
        Should.NotThrow(() => hostManager.Dispose());
    }
    
    [Fact]
    public void HubUrl_ReturnsValidUrl()
    {
        // Arrange
        var hostManager = new SignalRHostManager(4569,"myHub");

        // Act & Assert
        var hubUrl = hostManager.HubUrl;
        hubUrl.ShouldStartWith("http://");
        hubUrl.ShouldContain(":4569/myHub");
        //check if it contains valid ip address or localhost
        hubUrl.ShouldMatch(@"http://(localhost|\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):4569/myHub");
        hostManager.Dispose();
    }
    
    [Fact]
    public async Task Start_ThrowsObjectDisposedException_WhenManagerIsDisposed()
    {
        // Arrange
        var hostManager = new SignalRHostManager();
        await hostManager.Start(); // Start and dispose to set internal state
        hostManager.Dispose();

        // Act & Assert
        await Should.ThrowAsync<ObjectDisposedException>(async () => await hostManager.Start());
    }

    [Fact]
    public async Task Publisher_ThrowsObjectDisposedException_WhenManagerIsDisposed()
    {
        // Arrange
        var hostManager = new SignalRHostManager();
        await hostManager.Start(); // Start and dispose to set internal state
        hostManager.Dispose();

        // Act & Assert
        Should.Throw<ObjectDisposedException>(() => hostManager.Publisher);
    }

    [Fact]
    public void Publisher_ThrowsInvalidOperationException_WhenManagerNotStarted()
    {
        // Arrange
        var hostManager = new SignalRHostManager();

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => hostManager.Publisher);
    }
}
