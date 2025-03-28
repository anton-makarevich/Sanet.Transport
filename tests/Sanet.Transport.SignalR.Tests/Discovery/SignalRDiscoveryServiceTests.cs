using Sanet.Transport.SignalR.Discovery;
using Xunit;

namespace Sanet.Transport.SignalR.Tests.Discovery;

public class SignalRDiscoveryServiceTests
{
    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        // Arrange & Act
        using var service = new SignalRDiscoveryService();
        
        // Assert
        Assert.NotNull(service);
        service.Stop();
    }
    
    [Fact]
    public void StartListening_DoesNotThrow()
    {
        // Arrange - use a unique port for this test
        using var service = new SignalRDiscoveryService(5001);
        
        // Act & Assert - should not throw
        service.StartListening();
        
        // Cleanup
        service.Stop();
    }
    
    [Fact]
    public void BroadcastPresence_DoesNotThrow()
    {
        // Arrange - use a unique port for this test
        using var service = new SignalRDiscoveryService(5003);
        var hubUrl = "http://localhost:5000/transporthub";
        
        // Act & Assert - should not throw
        service.BroadcastPresence(hubUrl);
        
        // Cleanup
        service.Stop();
    }
    
    [Fact]
    public void Stop_DoesNotThrow()
    {
        // Arrange
        using var service = new SignalRDiscoveryService();
        
        // Act & Assert - should not throw
        service.Stop();
    }
    
    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Arrange
        var service = new SignalRDiscoveryService();
        
        // Act & Assert - should not throw
        service.Dispose();
    }
    
    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var service = new SignalRDiscoveryService();
        
        // Act & Assert - should not throw
        service.Dispose();
        service.Dispose(); // Second call should be safe
    }
    
    [Fact]
    public void HostDiscovered_CanSubscribeAndUnsubscribe()
    {
        // Arrange
        using var service = new SignalRDiscoveryService();
        var eventRaised = false;
        
        // Act
        Action<string> handler = _ => eventRaised = true;
        service.HostDiscovered += handler;
        service.HostDiscovered -= handler;
        
        // Assert - just testing that subscription operations don't throw
        Assert.False(eventRaised);
    }
}
