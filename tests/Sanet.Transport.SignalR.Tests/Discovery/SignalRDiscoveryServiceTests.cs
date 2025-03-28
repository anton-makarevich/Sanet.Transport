using Sanet.Transport.SignalR.Discovery;
using Shouldly;
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
        service.ShouldNotBeNull();
        service.Stop(); // Stop potential background task if any started implicitly (though none should)
    }
    
    [Fact]
    public void StartListening_DoesNotThrow()
    {
        // Arrange - use a unique port for this test
        using var service = new SignalRDiscoveryService();
        
        // Act & Assert - should not throw
        Should.NotThrow(() => service.StartListening());
        
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
        Should.NotThrow(() => service.BroadcastPresence(hubUrl));
        
        // Cleanup
        service.Stop();
    }
    
    [Fact]
    public void Stop_DoesNotThrow()
    {
        // Arrange
        using var service = new SignalRDiscoveryService();
        
        // Act & Assert - should not throw
        Should.NotThrow(() => service.Stop());
    }
    
    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Arrange
        var service = new SignalRDiscoveryService();
        
        // Act & Assert - should not throw
        Should.NotThrow(() => service.Dispose());
    }
    
    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var service = new SignalRDiscoveryService();
        
        // Act & Assert - should not throw
        service.Dispose();
        Should.NotThrow(() => service.Dispose()); // Second call should be safe
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
        eventRaised.ShouldBeFalse();
    }

    [Fact]
    public void BroadcastPresence_ThrowsWhenDisposed()
    {
        // Arrange
        var service = new SignalRDiscoveryService();
        service.Dispose();

        // Act & Assert
        Should.Throw<ObjectDisposedException>(() => service.BroadcastPresence("test"));
    }

    [Fact]
    public void StartListening_ThrowsWhenDisposed()
    {
        // Arrange
        var service = new SignalRDiscoveryService();
        service.Dispose();

        // Act & Assert
        Should.Throw<ObjectDisposedException>(() => service.StartListening());
    }

    [Fact]
    public async Task HostDiscovered_EventIsRaised_WhenHostBroadcasts()
    {
        // Arrange
        const int testPort = 5002; // Use a unique port for this test
        const string expectedUrl = "http://test.local:1234/hub";
        string? discoveredUrl = null;
        var discoveryComplete = new TaskCompletionSource<bool>();

        using var listeningService = new SignalRDiscoveryService(testPort);
        listeningService.HostDiscovered += url =>
        {
            discoveredUrl = url;
            discoveryComplete.TrySetResult(true); // Signal that discovery happened
        };

        using var broadcastingService = new SignalRDiscoveryService(testPort);

        try
        {
            // Act
            listeningService.StartListening();
            await Task.Delay(100); // Give listener a moment to start
            
            broadcastingService.BroadcastPresence(expectedUrl);

            // Wait for discovery or timeout
            var completedTask = await Task.WhenAny(discoveryComplete.Task, Task.Delay(TimeSpan.FromSeconds(10)));

            // Assert
            completedTask.ShouldBe(discoveryComplete.Task, "Discovery event should have been raised within the timeout.");
            discoveredUrl.ShouldBe(expectedUrl);
        }
        finally
        {
            // Cleanup
            listeningService.Stop();
            broadcastingService.Stop();
        }
    }
}
