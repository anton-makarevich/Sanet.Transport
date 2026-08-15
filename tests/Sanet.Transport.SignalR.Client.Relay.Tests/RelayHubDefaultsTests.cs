using Sanet.Transport.SignalR.Client.Relay;
using Shouldly;

namespace Sanet.Transport.SignalR.Client.Relay.Tests;

public class RelayHubDefaultsTests
{
    [Fact]
    public void BuildHubUrl_PlainBaseUrl_AppendsHubPath()
    {
        RelayHubDefaults.BuildHubUrl("https://hub.example.com")
            .ShouldBe("https://hub.example.com/hubs/relay");
    }

    [Theory]
    [InlineData("https://hub.example.com/", "https://hub.example.com/hubs/relay")]
    [InlineData("https://hub.example.com///", "https://hub.example.com/hubs/relay")]
    public void BuildHubUrl_TrailingSlashes_AreRemoved(string baseUrl, string expected)
    {
        RelayHubDefaults.BuildHubUrl(baseUrl).ShouldBe(expected);
    }

    [Theory]
    [InlineData("  https://hub.example.com  ", "https://hub.example.com/hubs/relay")]
    [InlineData(" https://hub.example.com/ ", "https://hub.example.com/hubs/relay")]
    public void BuildHubUrl_SurroundingWhitespace_IsTrimmed(string baseUrl, string expected)
    {
        RelayHubDefaults.BuildHubUrl(baseUrl).ShouldBe(expected);
    }
}
