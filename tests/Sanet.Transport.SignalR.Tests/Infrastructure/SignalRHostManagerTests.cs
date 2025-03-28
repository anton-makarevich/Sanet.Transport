using Sanet.Transport.SignalR.Infrastructure;
using Sanet.Transport.SignalR.Publishers;
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
        Assert.NotNull(hostManager);
    }
    
    [Fact]
    public void Constructor_WithCustomUrl_CreatesValidInstance()
    {
        // Arrange & Act
        const int customPort = 5001;
        var hostManager = new SignalRHostManager(customPort);
        
        // Assert
        Assert.NotNull(hostManager);
        // We can't easily test the URL was set correctly without exposing it
    }
    
    [Fact]
    public async Task Publisher_ReturnsNonNullInstance()
    {
        // Arrange
        var hostManager = new SignalRHostManager();
        await hostManager.StartAsync();
        
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
