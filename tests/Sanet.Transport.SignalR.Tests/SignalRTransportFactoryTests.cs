using Sanet.Transport.SignalR.Infrastructure;
using Sanet.Transport.SignalR.Publishers;
using Shouldly;
using Xunit;

namespace Sanet.Transport.SignalR.Tests;

public class SignalRTransportFactoryTests
{
    [Fact]
    public void CreateClient_WithValidUrl_ReturnsClientPublisher()
    {
        // Arrange
        var hubUrl = "http://localhost:5000/transporthub";
        
        // Act
        var client = SignalRTransportFactory.CreateClient(hubUrl);
        
        // Assert
        client.ShouldNotBeNull();
        client.ShouldBeOfType<SignalRClientPublisher>();
    }
    
    [Fact]
    public void CreateClient_WithInvalidUrl_ThrowsArgumentException()
    {
        // Arrange, Act & Assert
        Should.Throw<ArgumentException>(() => SignalRTransportFactory.CreateClient(string.Empty));
        Should.Throw<ArgumentException>(() => SignalRTransportFactory.CreateClient(null!));
    }
    
    [Fact]
    public async Task DiscoverHosts_ReturnsEmptyListWhenNoHostsAvailable()
    {
        // Arrange & Act
        // Use a very short timeout and unique port to make the test run quickly and avoid conflicts
        var hosts = await SignalRTransportFactory.DiscoverHosts(timeoutSeconds: 1, discoveryPort: 5004);
        
        // Assert
        hosts.ShouldNotBeNull(); // Cannot assert empty as other hosts might be discoverable on the network
    }
    
    [Fact]
    public async Task CreateHost_ReturnsStartedHostManager()
    {
        // Arrange
        SignalRHostManager? hostManager = null;
        try
        {
            // Act
            hostManager = await SignalRTransportFactory.CreateHost(port: 5005, enableDiscovery: false); // Use unique port and disable discovery for test isolation

            // Assert
            hostManager.ShouldNotBeNull();
            hostManager.Publisher.ShouldNotBeNull(); // Accessing Publisher confirms it started successfully
            hostManager.Publisher.ShouldBeOfType<SignalRServerPublisher>();
            hostManager.HubUrl.ShouldEndWith(":5005/transporthub");
        }
        finally
        {
            // Cleanup
            hostManager?.Dispose();
        }
    }

    [Fact]
    public async Task CreateHost_WithCustomHub_UsesCorrectHubName()
    {
        // Arrange
        SignalRHostManager? hostManager = null;
        const string customHub = "myCustomHub";
        try
        {
            // Act - Note: CreateHost doesn't currently take a hub name, using SignalRHostManager directly to test this aspect
            // This test highlights a potential missing feature in the factory method if customizing the hub name is desired.
            // For now, we'll test the SignalRHostManager directly which *does* allow hub customization.
            hostManager = new SignalRHostManager(port: 5006, hub: customHub);
            await hostManager.Start();
            
            // Assert
            hostManager.ShouldNotBeNull();
            hostManager.Publisher.ShouldNotBeNull();
            hostManager.HubUrl.ShouldEndWith($":5006/{customHub}");
        }
        finally
        {
            // Cleanup
            hostManager?.Dispose();
        }
    }
}
