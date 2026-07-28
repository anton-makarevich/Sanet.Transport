using Microsoft.AspNetCore.SignalR.Client;
using Sanet.Transport.SignalR.Client.Publishers;
using Shouldly;
using Xunit;

namespace Sanet.Transport.SignalR.Tests.Publishers;

public class RelayClientPublisherTests
{
    private const string ValidHubUrl = "http://localhost:5000/relayhub";
    private const string ValidRoomCode = "ROOM01";
    private const string ValidSessionToken = "token-abc-123";

    [Fact]
    public void Constructor_WithValidArgs_CreatesPublisher()
    {
        // Act
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidSessionToken);

        // Assert
        publisher.ShouldNotBeNull();
        publisher.State.ShouldBe(HubConnectionState.Disconnected);
        publisher.IsConnected.ShouldBeFalse();
    }

    [Theory]
    [InlineData("", ValidRoomCode, ValidSessionToken)]
    [InlineData(null, ValidRoomCode, ValidSessionToken)]
    [InlineData("   ", ValidRoomCode, ValidSessionToken)]
    public void Constructor_WithInvalidHubUrl_ThrowsArgumentException(string? hubUrl, string roomCode, string sessionToken)
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => new RelayClientPublisher(hubUrl!, roomCode, sessionToken));
    }

    [Theory]
    [InlineData(ValidHubUrl, "", ValidSessionToken)]
    [InlineData(ValidHubUrl, null, ValidSessionToken)]
    [InlineData(ValidHubUrl, "   ", ValidSessionToken)]
    public void Constructor_WithInvalidRoomCode_ThrowsArgumentException(string hubUrl, string? roomCode, string sessionToken)
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => new RelayClientPublisher(hubUrl, roomCode!, sessionToken));
    }

    [Theory]
    [InlineData(ValidHubUrl, ValidRoomCode, "")]
    [InlineData(ValidHubUrl, ValidRoomCode, null)]
    [InlineData(ValidHubUrl, ValidRoomCode, "   ")]
    public void Constructor_WithInvalidSessionToken_ThrowsArgumentException(string hubUrl, string roomCode, string? sessionToken)
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => new RelayClientPublisher(hubUrl, roomCode, sessionToken!));
    }

    [Fact]
    public void Subscribe_WithNullAction_ThrowsArgumentNullException()
    {
        // Arrange
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidSessionToken);

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => publisher.Subscribe(null!));
    }

    [Fact]
    public void Subscribe_ValidAction_RegistersSubscriberWithoutThrowing()
    {
        // Arrange
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidSessionToken);
        var called = false;

        // Act
        publisher.Subscribe(_ => called = true);

        // Assert
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task PublishMessage_WhenDisconnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidSessionToken);
        var message = new TransportMessage
        {
            MessageType = "TestCommand",
            SourceId = Guid.NewGuid(),
            Payload = "{}"
        };

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(() => publisher.PublishMessage(message));
    }

    [Fact]
    public async Task DisposeAsync_MarksPublisherDisposed()
    {
        // Arrange
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidSessionToken);

        // Act
        await publisher.DisposeAsync();

        // Assert
        await Should.ThrowAsync<ObjectDisposedException>(() => publisher.StartAsync());
        await Should.ThrowAsync<ObjectDisposedException>(() => publisher.PublishMessage(new TransportMessage
        {
            MessageType = "TestCommand",
            SourceId = Guid.NewGuid()
        }));
        Should.Throw<ObjectDisposedException>(() => publisher.Subscribe(_ => { }));
    }
}
