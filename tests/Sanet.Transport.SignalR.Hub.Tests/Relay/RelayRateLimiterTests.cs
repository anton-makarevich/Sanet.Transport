using Microsoft.Extensions.Options;
using Sanet.Transport.SignalR.Hub.Configuration;
using Sanet.Transport.SignalR.Hub.Relay;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests.Relay;

public class RelayRateLimiterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RemoveConnection_WithNullOrEmptyConnectionId_DoesNotThrow(string? connectionId)
    {
        var options = Options.Create(new HubOptions { RelayRateLimitPerMinute = 120 });
        var sut = new RelayRateLimiter(options, TimeProvider.System);

        var act = () => sut.RemoveConnection(connectionId!);

        act.ShouldNotThrow();
    }

    [Fact]
    public void RemoveConnection_WithValidId_RemovesStateSoWindowResets()
    {
        var options = Options.Create(new HubOptions { RelayRateLimitPerMinute = 1 });
        var sut = new RelayRateLimiter(options, TimeProvider.System);

        sut.TryConsume("conn-1").ShouldBeTrue();
        sut.TryConsume("conn-1").ShouldBeFalse();

        sut.RemoveConnection("conn-1");

        sut.TryConsume("conn-1").ShouldBeTrue();
    }
}
