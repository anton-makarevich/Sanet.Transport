using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sanet.Transport.SignalR.Client.Publishers;
using Sanet.Transport.SignalR.Client.Relay;
using Shouldly;
using Xunit;

namespace Sanet.Transport.SignalR.Tests.Publishers;

public class RelayClientPublisherTests
{
    private const string ValidHubUrl = "http://localhost:5000/relayhub";
    private const string ValidRoomCode = "ROOM01";
    private const string ValidRelayTicket = "token-abc-123";

    [Fact]
    public void Constructor_WithValidArgs_CreatesPublisher()
    {
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        // Act
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidRelayTicket, logger);

        // Assert
        publisher.ShouldNotBeNull();
        publisher.State.ShouldBe(HubConnectionState.Disconnected);
        publisher.IsConnected.ShouldBeFalse();
    }

    [Theory]
    [InlineData("", ValidRoomCode, ValidRelayTicket)]
    [InlineData(null, ValidRoomCode, ValidRelayTicket)]
    [InlineData("   ", ValidRoomCode, ValidRelayTicket)]
    public void Constructor_WithInvalidHubUrl_ThrowsArgumentException(string? hubUrl, string roomCode, string relayTicket)
    {
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        // Act & Assert
        Should.Throw<ArgumentException>(() => new RelayClientPublisher(hubUrl!, roomCode, relayTicket, logger));
    }

    [Theory]
    [InlineData(ValidHubUrl, "", ValidRelayTicket)]
    [InlineData(ValidHubUrl, null, ValidRelayTicket)]
    [InlineData(ValidHubUrl, "   ", ValidRelayTicket)]
    [InlineData(ValidHubUrl, "ABC", ValidRelayTicket)]
    [InlineData(ValidHubUrl, "ABCDEFG", ValidRelayTicket)]
    public void Constructor_WithInvalidRoomCode_ThrowsArgumentException(string hubUrl, string? roomCode, string relayTicket)
    {
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        // Act & Assert
        Should.Throw<ArgumentException>(() => new RelayClientPublisher(hubUrl, roomCode!, relayTicket, logger));
    }

    [Theory]
    [InlineData(ValidHubUrl, ValidRoomCode, "")]
    [InlineData(ValidHubUrl, ValidRoomCode, null)]
    [InlineData(ValidHubUrl, ValidRoomCode, "   ")]
    public void Constructor_WithInvalidRelayTicket_ThrowsArgumentException(string hubUrl, string roomCode, string? relayTicket)
    {
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        // Act & Assert
        Should.Throw<ArgumentException>(() => new RelayClientPublisher(hubUrl, roomCode, relayTicket!, logger));
    }

    [Fact]
    public void Subscribe_WithNullAction_ThrowsArgumentNullException()
    {
        // Arrange
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidRelayTicket, logger);

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => publisher.Subscribe(null!));
    }

    [Fact]
    public void Subscribe_ValidAction_RegistersSubscriberWithoutThrowing()
    {
        // Arrange
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidRelayTicket, logger);
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
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidRelayTicket, logger);
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
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidRelayTicket, logger);

        // Act
        await publisher.DisposeAsync();

        // Assert
        await Should.ThrowAsync<ObjectDisposedException>(publisher.StartAsync);
        await Should.ThrowAsync<ObjectDisposedException>(() => publisher.PublishMessage(new TransportMessage
        {
            MessageType = "TestCommand",
            SourceId = Guid.NewGuid()
        }));
        Should.Throw<ObjectDisposedException>(() => publisher.Subscribe(_ => { }));
    }

    [Fact]
    public async Task DisposeAsync_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidRelayTicket, logger);

        // Act & Assert
        await publisher.DisposeAsync();
        await Should.NotThrowAsync(async () => await publisher.DisposeAsync());
    }

    [Fact]
    public void Constructor_WithLoggerAndExpectedHostId_DoesNotThrow()
    {
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidRelayTicket, logger);
        publisher.ShouldNotBeNull();
        publisher.State.ShouldBe(HubConnectionState.Disconnected);
    }

    [Fact]
    public void Constructor_WithLoggerOnly_DoesNotThrow()
    {
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidRelayTicket, logger);
        publisher.ShouldNotBeNull();
    }

    [Fact]
    public void Constructor_WithExpectedHostIdOnly_DoesNotThrow()
    {
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidRelayTicket, logger);
        publisher.ShouldNotBeNull();
    }

    [Fact]
    public void HandleHubError_HostDisconnected_FiresHostDisconnectedEvent()
    {
        var publisher = CreatePublisher();
        var hostDisconnectedFired = false;
        publisher.HostDisconnected += () => hostDisconnectedFired = true;

        var method = typeof(RelayClientPublisher).GetMethod("HandleHubError",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.ShouldNotBeNull();

        var error = new HubError(HubErrorCode.HostDisconnected, "Host lost", ValidRoomCode);
        method.Invoke(publisher, [error]);

        hostDisconnectedFired.ShouldBeTrue();
    }

    [Fact]
    public void HandleHubError_OtherError_FiresHubErrorReceived()
    {
        var publisher = CreatePublisher();
        HubError? received = null;
        publisher.HubErrorReceived += e => received = e;

        var method = typeof(RelayClientPublisher).GetMethod("HandleHubError",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.ShouldNotBeNull();

        var error = new HubError(HubErrorCode.RoomNotFound, "Room not found", ValidRoomCode);
        method.Invoke(publisher, [error]);

        received.ShouldNotBeNull();
        received.Code.ShouldBe(HubErrorCode.RoomNotFound);
    }

    private static RelayClientPublisher CreatePublisher(ILogger<RelayClientPublisher>? logger = null)
    {
        logger ??= Substitute.For<ILogger<RelayClientPublisher>>();
        var original = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            return new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidRelayTicket, logger);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }
    }

    [Fact]
    public void TransportMessage_JsonRoundtrips()
    {
        var original = new TransportMessage
        {
            MessageType = "Test",
            SourceId = Guid.NewGuid(),
            Payload = "test"
        };
        var json = JsonSerializer.Serialize(original);
        json.ShouldNotBeNullOrEmpty();
        var deserialized = JsonSerializer.Deserialize<TransportMessage>(json);
        deserialized.ShouldNotBeNull();
        deserialized.MessageType.ShouldBe(original.MessageType);
    }

    [Fact]
    public void HandleEnvelopeReceived_WithExpectedHostId_AcceptsMatchingSender()
    {
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        var publisher = CreatePublisher(logger);
        var wasCalled = false;
        publisher.Subscribe(_ => wasCalled = true);

        var method = typeof(RelayClientPublisher).GetMethod("HandleEnvelopeReceived",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.ShouldNotBeNull();

        var message = new TransportMessage
        {
            MessageType = "Test",
            SourceId = Guid.NewGuid(),
            Payload = "test"
        };
        var serialized = JsonSerializer.Serialize(message);
        var validEnvelope = new RelayEnvelope(
            SenderId: "expected-host",
            Payload: serialized,
            SchemaVersion: "1.0.0",
            SequenceNumber: 1,
            Timestamp: DateTime.UtcNow);

        method.Invoke(publisher, [validEnvelope]);

        wasCalled.ShouldBeTrue();
    }

    [Fact]
    public void HandleEnvelopeReceived_MalformedPayload_DoesNotThrow()
    {
        var publisher = CreatePublisher();

        var method = typeof(RelayClientPublisher).GetMethod("HandleEnvelopeReceived",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.ShouldNotBeNull();

        var malformedEnvelope = new RelayEnvelope(
            SenderId: "sender-1",
            Payload: "not-json-at-all",
            SchemaVersion: "1.0.0",
            SequenceNumber: 1,
            Timestamp: DateTime.UtcNow);

        Should.NotThrow(() => method.Invoke(publisher, [malformedEnvelope]));
    }

    [Fact]
    public void HandleEnvelopeReceived_NullPayload_DoesNotThrow()
    {
        var publisher = CreatePublisher();

        var method = typeof(RelayClientPublisher).GetMethod("HandleEnvelopeReceived",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.ShouldNotBeNull();

        var envelope = new RelayEnvelope(
            SenderId: "sender-1",
            Payload: null!,
            SchemaVersion: "1.0.0",
            SequenceNumber: 1,
            Timestamp: DateTime.UtcNow);

        Should.NotThrow(() => method.Invoke(publisher, [envelope]));
    }

    [Fact]
    public void NotifySubscribers_UsesCapturedSynchronizationContext()
    {
        var postCalls = 0;
        var testSyncContext = new TestSynchronizationContext(() => postCalls++);

        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(testSyncContext);

        try
        {
            var logger = Substitute.For<ILogger<RelayClientPublisher>>();
            var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidRelayTicket, logger);
            var notified = false;
            publisher.Subscribe(_ => notified = true);

            var method = typeof(RelayClientPublisher).GetMethod("HandleEnvelopeReceived",
                BindingFlags.NonPublic | BindingFlags.Instance);
            method.ShouldNotBeNull();

            var message = new TransportMessage
            {
                MessageType = "Test",
                SourceId = Guid.NewGuid(),
                Payload = "test"
            };
            var serialized = JsonSerializer.Serialize(message);
            var envelope = new RelayEnvelope(
                SenderId: "sender-1",
                Payload: serialized,
                SchemaVersion: "1.0.0",
                SequenceNumber: 1,
                Timestamp: DateTime.UtcNow);

            method.Invoke(publisher, [envelope]);

            postCalls.ShouldBeGreaterThan(0);
            notified.ShouldBeTrue();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void HandleHubError_NonHostError_DoesNotFireHostDisconnected()
    {
        var publisher = CreatePublisher();
        var hostDisconnectedFired = false;
        publisher.HostDisconnected += () => hostDisconnectedFired = true;

        var method = typeof(RelayClientPublisher).GetMethod("HandleHubError",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.ShouldNotBeNull();

        var error = new HubError(HubErrorCode.RoomFull, "Room is full", ValidRoomCode);
        method.Invoke(publisher, [error]);

        hostDisconnectedFired.ShouldBeFalse();
    }

    [Fact]
    public void HandleEnvelopeReceived_NoExpectedHostId_DoesNotDropAnySender()
    {
        var publisher = CreatePublisher();
        var wasCalled = false;
        publisher.Subscribe(_ => wasCalled = true);

        var method = typeof(RelayClientPublisher).GetMethod("HandleEnvelopeReceived",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.ShouldNotBeNull();

        var message = new TransportMessage
        {
            MessageType = "Test",
            SourceId = Guid.NewGuid(),
            Payload = "test"
        };
        var serialized = JsonSerializer.Serialize(message);
        var envelope = new RelayEnvelope(
            SenderId: "any-sender",
            Payload: serialized,
            SchemaVersion: "1.0.0",
            SequenceNumber: 1,
            Timestamp: DateTime.UtcNow);

        method.Invoke(publisher, [envelope]);

        wasCalled.ShouldBeTrue();
    }

    [Fact]
    public void BuildConnectionUrl_AppendsRelayTicket()
    {
        var url = RelayClientPublisher.BuildConnectionUrl(ValidHubUrl, ValidRelayTicket);

        url.ShouldBe($"http://localhost:5000/relayhub?ticket={Uri.EscapeDataString(ValidRelayTicket)}");
    }

    [Fact]
    public void BuildConnectionUrl_WithExistingQuery_AppendsParameters()
    {
        var url = RelayClientPublisher.BuildConnectionUrl("http://localhost:5000/relayhub?foo=bar", ValidRelayTicket);

        url.ShouldBe(
            $"http://localhost:5000/relayhub?foo=bar&ticket={Uri.EscapeDataString(ValidRelayTicket)}");
    }

    [Fact]
    public void BuildConnectionUrl_WithExistingRelayTicket_ReplacesItAndPreservesOtherParameters()
    {
        var url = RelayClientPublisher.BuildConnectionUrl(
            "http://localhost:5000/relayhub?ticket=old-token&foo=bar", ValidRelayTicket);

        url.ShouldBe(
            $"http://localhost:5000/relayhub?foo=bar&ticket={Uri.EscapeDataString(ValidRelayTicket)}");
    }

    [Fact]
    public void BuildConnectionUrl_WithExistingRelayTicketOnly_ReplacesIt()
    {
        var url = RelayClientPublisher.BuildConnectionUrl(
            "http://localhost:5000/relayhub?ticket=old-token", ValidRelayTicket);

        url.ShouldBe(
            $"http://localhost:5000/relayhub?ticket={Uri.EscapeDataString(ValidRelayTicket)}");
    }

    [Fact]
    public void BuildConnectionUrl_EscapesSpecialCharactersInToken()
    {
        var url = RelayClientPublisher.BuildConnectionUrl(ValidHubUrl, "tok+en/=");

        url.ShouldBe("http://localhost:5000/relayhub?ticket=tok%2Ben%2F%3D");
    }

    [Fact]
    public void RelayTicketExpiryRetryPolicy_WhenTicketAlreadyExpired_ReturnsNull()
    {
        var policy = new RelayTicketExpiryRetryPolicy(DateTimeOffset.UtcNow.AddSeconds(-1));

        policy.NextRetryDelay(CreateRetryContext(previousRetryCount: 0, elapsedTime: TimeSpan.Zero)).ShouldBeNull();
    }

    [Fact]
    public void RelayTicketExpiryRetryPolicy_WhenRetryWindowExhaustedBeforeTicketExpiry_ReturnsNull()
    {
        var policy = new RelayTicketExpiryRetryPolicy(DateTimeOffset.UtcNow.AddSeconds(5), TimeSpan.FromSeconds(2));

        // The reconnect window must end before the ticket expires: with a 3s elapsed
        // window against a 5s ticket and 2s margin, no further retry may be attempted.
        policy.NextRetryDelay(CreateRetryContext(previousRetryCount: 10, elapsedTime: TimeSpan.FromSeconds(3))).ShouldBeNull();
    }

    [Fact]
    public void RelayTicketExpiryRetryPolicy_WhenNextDelayExceedsRemainingWindow_ReturnsNull()
    {
        var policy = new RelayTicketExpiryRetryPolicy(DateTimeOffset.UtcNow.AddSeconds(3.5), TimeSpan.FromSeconds(2));

        // Only 1.5s fit before the margin; the 2s retry delay would overshoot, so stop.
        policy.NextRetryDelay(CreateRetryContext(previousRetryCount: 1, elapsedTime: TimeSpan.Zero)).ShouldBeNull();
    }

    [Fact]
    public void RelayTicketExpiryRetryPolicy_WithinTicketWindow_ReturnsBoundedRetryDelay()
    {
        var policy = new RelayTicketExpiryRetryPolicy(DateTimeOffset.UtcNow.AddSeconds(30), TimeSpan.FromSeconds(2));

        policy.NextRetryDelay(CreateRetryContext(previousRetryCount: 0, elapsedTime: TimeSpan.Zero))
            .ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task StartAsync_WhenConnectionDropsInsideTicketWindow_ReconnectsAutomatically()
    {
        await using var host = await FlakyTestRelayHubHost.StartAsync(ValidRelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        await using var publisher = new RelayClientPublisher(
            hubUrl,
            ValidRoomCode,
            ValidRelayTicket,
            logger,
            DateTimeOffset.UtcNow.AddSeconds(30));

        var reconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.Reconnected += _ => reconnected.TrySetResult(true);

        await publisher.StartAsync();
        publisher.IsConnected.ShouldBeTrue();

        var completed = await Task.WhenAny(reconnected.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.ShouldBe(reconnected.Task, "Expected automatic reconnect while the relay ticket is still valid");
        publisher.IsConnected.ShouldBeTrue();
    }

    [Fact]
    public async Task StartAsync_WhenConnectionDropsWithoutTicketExpiry_ClosesWithoutReconnecting()
    {
        await using var host = await FlakyTestRelayHubHost.StartAsync(ValidRelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        await using var publisher = new RelayClientPublisher(hubUrl, ValidRoomCode, ValidRelayTicket, logger);

        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.Closed += _ => closed.TrySetResult(true);

        await publisher.StartAsync();
        publisher.IsConnected.ShouldBeTrue();

        var completed = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.ShouldBe(closed.Task, "Expected connection to close without automatic reconnect");
    }

    private static RetryContext CreateRetryContext(long previousRetryCount, TimeSpan elapsedTime) => new()
    {
        PreviousRetryCount = previousRetryCount,
        ElapsedTime = elapsedTime,
        RetryReason = null
    };

    private sealed class TestSynchronizationContext(Action onPost) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            onPost();
            d(state);
        }
    }
}
