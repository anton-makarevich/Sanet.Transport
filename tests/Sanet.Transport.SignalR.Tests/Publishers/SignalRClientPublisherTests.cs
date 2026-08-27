using System.Net.Http;
using Sanet.Transport.SignalR.Client.Publishers;
using Shouldly;
using Xunit;

namespace Sanet.Transport.SignalR.Tests.Publishers;

public class SignalRClientPublisherTests
{
    [Fact]
    public void PublishMessage_CreatesPublisher()
    {
        // This test requires mocking HubConnection which is challenging
        // In a real implementation, we would use integration tests with a real hub
        
        // For now, we'll just verify the client publisher can be constructed
        const string hubUrl = "http://localhost:5000/transporthub";
        var publisher = new SignalRClientPublisher(hubUrl);
        
        // Assert that the publisher was created successfully
        publisher.ShouldNotBeNull();
    }
    
    [Fact]
    public void Constructor_WithNullOrEmptyUrl_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Should.Throw<ArgumentException>(() => new SignalRClientPublisher(string.Empty));
        Should.Throw<ArgumentException>(() => new SignalRClientPublisher(null!));
    }
    
    [Fact]
    public void Subscribe_AddsSubscriber()
    {
        // Arrange
        const string hubUrl = "http://localhost:5000/transporthub";
        var publisher = new SignalRClientPublisher(hubUrl);
        
        var messageReceived = false;
        
        // Act
        publisher.Subscribe(_ => messageReceived = true);
        
        // We can't easily test the subscription directly without integration tests
        // This test just verifies the method doesn't throw
        messageReceived.ShouldBeFalse();
    }

    [Fact]
    public async Task DisposeAsync_DisposesPublisher()
    {
        // Arrange
        const string hubUrl = "http://localhost:5000/transporthub";
        var publisher = new SignalRClientPublisher(hubUrl);

        // Act
        await publisher.DisposeAsync();

        // Assert
        await Should.ThrowAsync<ObjectDisposedException>(() => publisher.StartAsync());
        Should.Throw<ObjectDisposedException>(() => publisher.Subscribe(_ => { }));
        await Should.NotThrowAsync(() => publisher.PublishMessage(new TransportMessage
        {
            MessageType = "TestCommand",
            SourceId = Guid.NewGuid()
        }));
    }

    [Fact]
    public async Task DisposeAsync_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        const string hubUrl = "http://localhost:5000/transporthub";
        var publisher = new SignalRClientPublisher(hubUrl);

        // Act & Assert
        await publisher.DisposeAsync();
        await Should.NotThrowAsync(async () => await publisher.DisposeAsync());
    }

    [Fact]
    public void ConnectionState_BeforeStart_IsDisconnected()
    {
        // Arrange
        const string hubUrl = "http://localhost:5000/transporthub";
        var publisher = new SignalRClientPublisher(hubUrl);

        // Assert
        publisher.ConnectionState.ShouldBe(TransportConnectionState.Disconnected);
    }

    [Fact]
    public async Task ConnectionState_AfterStart_IsConnected()
    {
        // Arrange
        await using var app = await LanTestHubHost.StartAsync();
        await using var publisher = new SignalRClientPublisher(app.Urls.First().TrimEnd('/') + "/hubs/lan");

        // Act
        await publisher.StartAsync();

        // Assert
        publisher.ConnectionState.ShouldBe(TransportConnectionState.Connected);
    }

    [Fact]
    public async Task ConnectionStateChanged_AfterStart_RaisesConnectingThenConnected()
    {
        // Arrange
        await using var app = await LanTestHubHost.StartAsync();
        await using var publisher = new SignalRClientPublisher(app.Urls.First().TrimEnd('/') + "/hubs/lan");
        var states = new List<TransportConnectionState>();
        publisher.ConnectionStateChanged += states.Add;

        // Act
        await publisher.StartAsync();
        await WaitUntilAsync(() => states.Contains(TransportConnectionState.Connected));

        // Assert
        states.ShouldContain(TransportConnectionState.Connecting);
        states.ShouldContain(TransportConnectionState.Connected);
        states[^1].ShouldBe(TransportConnectionState.Connected);
    }

    [Fact]
    public async Task ConnectionStateChanged_AfterConnectionDropAndReconnect_RaisesReconnectingThenConnected()
    {
        // Arrange
        await using var app = await LanTestHubHost.StartAsync();
        await using var publisher = new SignalRClientPublisher(app.Urls.First().TrimEnd('/') + "/hubs/lan");
        var states = new List<TransportConnectionState>();
        publisher.ConnectionStateChanged += states.Add;

        // Act
        await publisher.StartAsync();
        await WaitUntilAsync(() => states.Contains(TransportConnectionState.Connected));

        // The hub aborts the first connection shortly after connect, triggering automatic
        // reconnect. Verify the visual state tracks the drop and the recovery.
        await WaitUntilAsync(() => states.Contains(TransportConnectionState.Reconnecting));

        // Assert
        states.ShouldContain(TransportConnectionState.Connected);
        publisher.ConnectionState.ShouldBe(TransportConnectionState.Connected);
    }

    [Fact]
    public async Task ConnectionState_WhenStartFails_IsDisconnectedAndEventRaised()
    {
        // A port that is guaranteed to have no listener causes the connection attempt to fail.
        const string hubUrl = "http://127.0.0.1:9/hubs/lan";
        await using var publisher = new SignalRClientPublisher(hubUrl);
        var states = new List<TransportConnectionState>();
        publisher.ConnectionStateChanged += states.Add;

        // Act & Assert
        await Should.ThrowAsync<HttpRequestException>(() => publisher.StartAsync());

        publisher.ConnectionState.ShouldBe(TransportConnectionState.Disconnected);
        states.ShouldContain(TransportConnectionState.Connecting);
        states[^1].ShouldBe(TransportConnectionState.Disconnected);
    }

    [Fact]
    public async Task ConnectionState_AfterDispose_IsClosedAndEventRaisedOnce()
    {
        // Arrange
        await using var app = await LanTestHubHost.StartAsync();
        await using var publisher = new SignalRClientPublisher(app.Urls.First().TrimEnd('/') + "/hubs/lan");
        var states = new List<TransportConnectionState>();
        publisher.ConnectionStateChanged += states.Add;
        await publisher.StartAsync();
        await WaitUntilAsync(() => states.Contains(TransportConnectionState.Connected));

        // Act
        await publisher.DisposeAsync();

        // Assert
        publisher.ConnectionState.ShouldBe(TransportConnectionState.Closed);
        states.Last().ShouldBe(TransportConnectionState.Closed);
        states.Count(s => s == TransportConnectionState.Closed).ShouldBe(1);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        predicate().ShouldBeTrue();
    }
}
