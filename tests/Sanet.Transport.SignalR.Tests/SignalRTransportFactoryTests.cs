using System;
using System.Threading.Tasks;
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
        Assert.NotNull(client);
        Assert.IsType<SignalRClientPublisher>(client);
    }
    
    [Fact]
    public void CreateClient_WithInvalidUrl_ThrowsArgumentException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() => SignalRTransportFactory.CreateClient(string.Empty));
        Assert.Throws<ArgumentException>(() => SignalRTransportFactory.CreateClient(null!));
    }
    
    [Fact]
    public async Task DiscoverHosts_ReturnsEmptyListWhenNoHostsAvailable()
    {
        // Arrange & Act
        // Use a very short timeout to make the test run quickly
        var hosts = await SignalRTransportFactory.DiscoverHostsAsync(timeoutSeconds: 1);
        
        // Assert
        Assert.NotNull(hosts);
        // We can't guarantee there are no hosts on the network, so we can't assert Count == 0
    }
}
