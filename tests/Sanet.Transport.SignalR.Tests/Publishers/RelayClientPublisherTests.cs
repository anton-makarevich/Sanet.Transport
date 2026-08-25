using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
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
    private const string RefreshedRelayTicket = "token-refreshed-456";

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

    [Fact]
    public async Task StartAsync_WhenConnectionDropsAndRefreshReturnsFreshTicket_RestartsConnectionWithoutClosing()
    {
        await using var host = await RebuildTestRelayHubHost.StartAsync(
            [ValidRelayTicket, RefreshedRelayTicket],
            abortTicket: ValidRelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        await using var publisher = new RelayClientPublisher(
            hubUrl,
            ValidRoomCode,
            ValidRelayTicket,
            logger,
            relayTicketExpiresAt: null,
            _ => Task.FromResult<RelayTicketRefresh?>(new RelayTicketRefresh(
                RefreshedRelayTicket, DateTimeOffset.UtcNow.AddSeconds(60))));

        var reconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.Reconnected += _ => reconnected.TrySetResult(true);
        publisher.Closed += _ => closed.TrySetResult(true);

        await publisher.StartAsync();
        publisher.IsConnected.ShouldBeTrue();

        var completed = await Task.WhenAny(reconnected.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.ShouldBe(reconnected.Task, "Expected manual rebuild with a fresh ticket to succeed");
        publisher.IsConnected.ShouldBeTrue();
        closed.Task.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task StartAsync_WhenConnectionDropsAndRefreshReturnsNull_RaisesTerminalClosed()
    {
        await using var host = await RebuildTestRelayHubHost.StartAsync(
            [ValidRelayTicket],
            abortTicket: ValidRelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        await using var publisher = new RelayClientPublisher(
            hubUrl,
            ValidRoomCode,
            ValidRelayTicket,
            logger,
            relayTicketExpiresAt: null,
            _ => Task.FromResult<RelayTicketRefresh?>(null));

        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.Closed += _ => closed.TrySetResult(true);

        await publisher.StartAsync();
        publisher.IsConnected.ShouldBeTrue();

        var completed = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.ShouldBe(closed.Task, "Expected terminal Closed when ticket refresh fails");
        publisher.IsConnected.ShouldBeFalse();
    }

    [Fact]
    public async Task StartAsync_WhenConnectionDropsAndRefreshThrows_RaisesTerminalClosed()
    {
        await using var host = await RebuildTestRelayHubHost.StartAsync(
            [ValidRelayTicket],
            abortTicket: ValidRelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        await using var publisher = new RelayClientPublisher(
            hubUrl,
            ValidRoomCode,
            ValidRelayTicket,
            logger,
            relayTicketExpiresAt: null,
            _ => Task.FromException<RelayTicketRefresh?>(new InvalidOperationException("refresh failed")));

        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.Closed += _ => closed.TrySetResult(true);

        await publisher.StartAsync();
        publisher.IsConnected.ShouldBeTrue();

        var completed = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.ShouldBe(closed.Task, "Expected terminal Closed when ticket refresh throws");
    }

    [Fact]
    public async Task DisposeAsync_CancelsInFlightRebuildAndPreventsFurtherTicketRefresh()
    {
        await using var host = await RebuildTestRelayHubHost.StartAsync(
            [ValidRelayTicket],
            abortTicket: ValidRelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        var refreshInvocations = 0;
        var refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        var publisher = new RelayClientPublisher(
            hubUrl,
            ValidRoomCode,
            ValidRelayTicket,
            logger,
            relayTicketExpiresAt: null,
            ct =>
            {
                Interlocked.Increment(ref refreshInvocations);
                refreshStarted.TrySetResult(true);
                return Task.Delay(Timeout.InfiniteTimeSpan, ct)
                    .ContinueWith<RelayTicketRefresh?>(_ => null, TaskScheduler.Default);
            });

        await publisher.StartAsync();
        publisher.IsConnected.ShouldBeTrue();

        // Wait until the connection dropped and the rebuild is blocked in the refresh.
        var started = await Task.WhenAny(refreshStarted.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        started.ShouldBe(refreshStarted.Task, "expected the rebuild to start after the drop");

        // Disposal must cancel the in-flight refresh and await its completion.
        await publisher.DisposeAsync();

        // The rebuild has fully unwound before DisposeAsync returns, and no further
        // close notification can occur afterwards, so exactly one invocation happened.
        Interlocked.CompareExchange(ref refreshInvocations, 0, 0).ShouldBe(1);
        publisher.IsConnected.ShouldBeFalse();
    }

    [Fact]
    public async Task OnClosed_DuringInFlightRebuild_RetainsSingleFollowUpPassWithoutExtraRefresh()
    {
        await using var host = await RebuildTestRelayHubHost.StartAsync(
            [ValidRelayTicket, RefreshedRelayTicket],
            abortTicket: ValidRelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        var refreshInvocations = 0;
        var refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        await using var publisher = new RelayClientPublisher(
            hubUrl,
            ValidRoomCode,
            ValidRelayTicket,
            logger,
            relayTicketExpiresAt: null,
            async _ =>
            {
                Interlocked.Increment(ref refreshInvocations);
                refreshStarted.TrySetResult(true);
                await releaseRefresh.Task;
                return new RelayTicketRefresh(
                    RefreshedRelayTicket, DateTimeOffset.UtcNow.AddSeconds(60));
            });

        var closedCount = 0;
        publisher.Closed += _ => Interlocked.Increment(ref closedCount);

        await publisher.StartAsync();
        publisher.IsConnected.ShouldBeTrue();

        // Wait until the drop started a rebuild that is blocked inside the refresh
        // delegate; the single-consumer loop is now provably busy processing pass one.
        var started = await Task.WhenAny(refreshStarted.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        started.ShouldBe(refreshStarted.Task, "expected the rebuild to start after the drop");

        // A further close notification arrives while the rebuild is in flight. The
        // bounded channel must retain it as exactly one queued follow-up signal rather
        // than spawning a concurrent rebuild.
        var onClosed = typeof(RelayClientPublisher).GetMethod("OnClosed",
            BindingFlags.NonPublic | BindingFlags.Instance);
        onClosed.ShouldNotBeNull();
        var currentConnectionField = typeof(RelayClientPublisher).GetField("_hubConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);
        currentConnectionField.ShouldNotBeNull();
        var currentConnection = (HubConnection)currentConnectionField.GetValue(publisher)!;
        await (Task)onClosed.Invoke(publisher, [currentConnection, null])!;

        // Complete the in-flight rebuild with a fresh ticket.
        releaseRefresh.TrySetResult(true);

        // The rebuilt connection stays up, so the follow-up pass observes it already
        // connected and must not perform another ticket refresh nor raise Closed.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!publisher.IsConnected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        // Yield so the follow-up pass has time to run before asserting on its effects.
        await Task.Delay(500);

        publisher.IsConnected.ShouldBeTrue("the rebuilt connection must remain connected");
        Interlocked.CompareExchange(ref refreshInvocations, 0, 0).ShouldBe(1,
            "the coalesced follow-up pass must not trigger a second ticket refresh once the connection recovered");
        Volatile.Read(ref closedCount).ShouldBe(0,
            "no terminal Closed may be raised when both passes complete successfully");
    }

    [Fact]
    public async Task DisposeAsync_WhenNotStarted_RaisesClosedExactlyOnce()
    {
        // Arrange
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        var publisher = new RelayClientPublisher(ValidHubUrl, ValidRoomCode, ValidRelayTicket, logger);
        var closedCount = 0;
        publisher.Closed += _ => Interlocked.Increment(ref closedCount);

        // Act
        await publisher.DisposeAsync();
        await publisher.DisposeAsync();

        // Closed is delivered via the captured synchronization context; yield until it arrives.
        await WaitUntilAsync(() => Volatile.Read(ref closedCount) > 0);

        // Assert
        Interlocked.CompareExchange(ref closedCount, 0, 0).ShouldBe(1,
            "Closed must be raised exactly once during disposal");
    }

    [Fact]
    public async Task DisposeAsync_WhenConnected_RaisesClosedExactlyOnce()
    {
        await using var host = await RebuildTestRelayHubHost.StartAsync([ValidRelayTicket], abortTicket: ValidRelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        var publisher = new RelayClientPublisher(
            hubUrl,
            ValidRoomCode,
            ValidRelayTicket,
            logger,
            relayTicketExpiresAt: null,
            ct => Task.Delay(Timeout.InfiniteTimeSpan, ct)
                .ContinueWith<RelayTicketRefresh?>(_ => null, TaskScheduler.Default));

        var closedCount = 0;
        publisher.Closed += _ => Interlocked.Increment(ref closedCount);

        await publisher.StartAsync();
        publisher.IsConnected.ShouldBeTrue();

        await publisher.DisposeAsync();

        publisher.IsConnected.ShouldBeFalse();

        // Closed is delivered via the captured synchronization context; yield until it arrives.
        await WaitUntilAsync(() => Volatile.Read(ref closedCount) > 0);

        Interlocked.CompareExchange(ref closedCount, 0, 0).ShouldBe(1,
            "Closed must be raised exactly once even when stopping the connection fires close notifications");
    }

    [Fact]
    public async Task PublishMessage_WhenDisconnected_ThrowsTransportPublishExceptionNotConnected()
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
        var exception = await Should.ThrowAsync<TransportPublishException>(
            () => publisher.PublishMessage(message));
        exception.Reason.ShouldBe(PublishFailureReason.NotConnected);
    }

    [Fact]
    public async Task PublishMessage_WhileRebuildInProgress_QueuesMessagesAndDeliversInOrderAfterReconnect()
    {
        await using var host = await RebuildTestRelayHubHost.StartAsync(
            [ValidRelayTicket, RefreshedRelayTicket],
            abortTicket: ValidRelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        var refreshGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        await using var publisher = new RelayClientPublisher(
            hubUrl,
            ValidRoomCode,
            ValidRelayTicket,
            logger,
            relayTicketExpiresAt: null,
            async _ =>
            {
                await refreshGate.Task;
                return new RelayTicketRefresh(
                    RefreshedRelayTicket, DateTimeOffset.UtcNow.AddSeconds(60));
            });

        var received = new List<string>();
        var allReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.Subscribe(message =>
        {
            lock (received)
            {
                received.Add(message.Payload);
                if (received.Count == 3) allReceived.TrySetResult(true);
            }
        });

        await publisher.StartAsync();
        publisher.IsConnected.ShouldBeTrue();

        // Wait for the drop and the blocked refresh (rebuild in progress).
        await WaitUntilAsync(() => !publisher.IsConnected);

        // Publishing while the rebuild is in flight must queue, not throw.
        foreach (var payload in new[] { "m1", "m2", "m3" })
        {
            await publisher.PublishMessage(new TransportMessage
            {
                MessageType = "TestCommand",
                SourceId = Guid.NewGuid(),
                Payload = payload
            });
        }

        refreshGate.TrySetResult(true);

        var completed = await Task.WhenAny(allReceived.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.ShouldBe(allReceived.Task, "Expected queued messages to be delivered after rebuild");
        lock (received)
        {
            received.ShouldBe(["m1", "m2", "m3"]);
        }
    }

    [Fact]
    public async Task PublishMessage_WhenQueueOverflows_ThrowsQueueFullAndKeepsEarlierMessages()
    {
        await using var host = await RebuildTestRelayHubHost.StartAsync(
            [ValidRelayTicket, RefreshedRelayTicket],
            abortTicket: ValidRelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        var refreshGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        await using var publisher = new RelayClientPublisher(
            hubUrl,
            ValidRoomCode,
            ValidRelayTicket,
            logger,
            relayTicketExpiresAt: null,
            async ct =>
            {
                // Hold the rebuild in-flight while messages are published; disposal
                // cancels the token, ending the hold without invoking real refresh.
                try
                {
                    await refreshGate.Task.WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                }

                return null;
            },
            outboundQueueCapacity: 2);

        await publisher.StartAsync();
        await WaitUntilAsync(() => !publisher.IsConnected);

        TransportMessage Make(string payload) => new()
        {
            MessageType = "TestCommand",
            SourceId = Guid.NewGuid(),
            Payload = payload
        };

        await publisher.PublishMessage(Make("q1"));
        await publisher.PublishMessage(Make("q2"));
        var overflow = await Should.ThrowAsync<TransportPublishException>(
            () => publisher.PublishMessage(Make("q3")));
        overflow.Reason.ShouldBe(PublishFailureReason.QueueFull);
    }

    [Fact]
    public async Task PublishMessage_DuringAutoReconnectDrain_QueuesAndDelivers()
    {
        await using var host = await AutoReconnectTestRelayHubHost.StartAsync(ValidRelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        await using var publisher = new RelayClientPublisher(
            hubUrl,
            ValidRoomCode,
            ValidRelayTicket,
            logger,
            DateTimeOffset.UtcNow.AddSeconds(30));

        var received = new List<string>();
        var allReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.Subscribe(message =>
        {
            lock (received)
            {
                received.Add(message.Payload);
                if (received.Count >= 1) allReceived.TrySetResult(true);
            }
        });

        // Publish a message inside the Reconnected handler.  Because _isTransitioning
        // is set before Reconnected fires, PublishMessage must queue the message so
        // FlushOutboundQueueAsync drains it in order.
        publisher.Reconnected += _ =>
        {
            publisher.PublishMessage(new TransportMessage
            {
                MessageType = "TestCommand",
                SourceId = Guid.NewGuid(),
                Payload = "during-drain"
            }).GetAwaiter().GetResult();
        };

        await publisher.StartAsync();
        publisher.IsConnected.ShouldBeTrue();

        var completed = await Task.WhenAny(allReceived.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        completed.ShouldBe(allReceived.Task,
            "Expected message published during auto-reconnect drain to be delivered");
        lock (received)
        {
            received.ShouldBe(["during-drain"]);
        }
    }

    [Fact]
    public async Task DrainQueue_InvocationFailureRetried_MessagesDeliveredInOrder()
    {
        await using var host = await FailInvocationRelayHubHost.StartAsync(ValidRelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        await using var publisher = new RelayClientPublisher(
            hubUrl,
            ValidRoomCode,
            ValidRelayTicket,
            logger,
            DateTimeOffset.UtcNow.AddSeconds(30));

        var received = new List<string>();
        var allReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.Subscribe(message =>
        {
            lock (received)
            {
                received.Add(message.Payload);
                if (received.Count == 2) allReceived.TrySetResult(true);
            }
        });

        // Publish both messages inside the Reconnected handler.  _isTransitioning is
        // already true, so PublishMessage queues them for FlushOutboundQueueAsync to
        // drain. The first drain attempt fails (relay attempt 1), triggering
        // DrainOutboundQueueAsync which retries until the server accepts both.
        // This avoids synchronizing on the Reconnected event itself, which is unreliable
        // when the test host aborts connections aggressively.
        publisher.Reconnected += _ =>
        {
            publisher.PublishMessage(new TransportMessage
            {
                MessageType = "TestCommand",
                SourceId = Guid.NewGuid(),
                Payload = "m1"
            }).GetAwaiter().GetResult();
            publisher.PublishMessage(new TransportMessage
            {
                MessageType = "TestCommand",
                SourceId = Guid.NewGuid(),
                Payload = "m2"
            }).GetAwaiter().GetResult();
        };

        await publisher.StartAsync();
        publisher.IsConnected.ShouldBeTrue();

        var completed = await Task.WhenAny(allReceived.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.ShouldBe(allReceived.Task,
            "Expected all messages delivered in order after invocation failure retry");
        lock (received)
        {
            received.ShouldBe(["m1", "m2"]);
        }
    }

    [Fact]
    public async Task DrainQueue_RepeatedInvocationFailures_PublishDuringDrainDeliveredInOrder()
    {
        await using var host = await DrainRetryRelayHubHost.StartAsync(ValidRelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";
        var relayCounter = host.Services.GetRequiredService<DrainInvocationCounter>();
        var drainGate = host.Services.GetRequiredService<DrainGate>();

        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        await using var publisher = new RelayClientPublisher(
            hubUrl,
            ValidRoomCode,
            ValidRelayTicket,
            logger,
            DateTimeOffset.UtcNow.AddSeconds(30));

        var received = new List<string>();
        var allReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.Subscribe(message =>
        {
            lock (received)
            {
                received.Add(message.Payload);
                if (received.Count == 3) allReceived.TrySetResult(true);
            }
        });

        // Publish two messages inside the Reconnected handler so they are queued for
        // the post-reconnect drain. The hub then fails DrainRetryRelayHub.FailUntilRelayCount
        // consecutive invocations while still connected, exercising more retries than the
        // drain previously allowed before clearing its recovery gate.
        publisher.Reconnected += _ =>
        {
            foreach (var payload in new[] { "m1", "m2" })
            {
                publisher.PublishMessage(new TransportMessage
                {
                    MessageType = "TestCommand",
                    SourceId = Guid.NewGuid(),
                    Payload = payload
                }).GetAwaiter().GetResult();
            }
        };

        await publisher.StartAsync();
        publisher.IsConnected.ShouldBeTrue();

        // Once all configured invocation failures have been consumed by the drain,
        // publish another message: it must be appended to the queue (not sent directly),
        // preserving FIFO delivery ahead of the blocked-but-pending queue contents.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (relayCounter.Count < DrainRetryRelayHub.FailUntilRelayCount && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        relayCounter.Count.ShouldBeGreaterThanOrEqualTo(DrainRetryRelayHub.FailUntilRelayCount);

        await publisher.PublishMessage(new TransportMessage
        {
            MessageType = "TestCommand",
            SourceId = Guid.NewGuid(),
            Payload = "m3"
        });

        drainGate.Release();

        var completed = await Task.WhenAny(allReceived.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        completed.ShouldBe(allReceived.Task,
            "Expected all messages delivered in order after repeated invocation failures");
        lock (received)
        {
            received.ShouldBe(["m1", "m2", "m3"]);
        }
    }

    [Fact]
    public async Task DrainQueue_AfterManualRebuild_InvocationFailureRetriedAndDirectPublishQueued()
    {
        await using var host = await RebuildDrainRetryRelayHubHost.StartAsync(
            [ValidRelayTicket, RefreshedRelayTicket],
            abortTicket: ValidRelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";
        var relayCounter = host.Services.GetRequiredService<DrainInvocationCounter>();

        var logger = Substitute.For<ILogger<RelayClientPublisher>>();
        var refreshGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var publisher = new RelayClientPublisher(
            hubUrl,
            ValidRoomCode,
            ValidRelayTicket,
            logger,
            relayTicketExpiresAt: null,
            async _ =>
            {
                await refreshGate.Task;
                return new RelayTicketRefresh(
                    RefreshedRelayTicket, DateTimeOffset.UtcNow.AddSeconds(60));
            });

        var received = new List<string>();
        var allReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.Subscribe(message =>
        {
            lock (received)
            {
                received.Add(message.Payload);
                if (received.Count == 3) allReceived.TrySetResult(true);
            }
        });

        await publisher.StartAsync();
        publisher.IsConnected.ShouldBeTrue();

        // Wait for the drop; messages published during the rebuild must be queued.
        // The refresh is gated so the rebuild stays pending until the messages are queued.
        await WaitUntilAsync(() => !publisher.IsConnected);
        foreach (var payload in new[] { "m1", "m2" })
        {
            await publisher.PublishMessage(new TransportMessage
            {
                MessageType = "TestCommand",
                SourceId = Guid.NewGuid(),
                Payload = payload
            });
        }

        refreshGate.TrySetResult(true);

        // Once the rebuilt connection's drain has consumed the configured invocation
        // failures (the replacement connection stays connected), a direct publish must
        // still be appended to the queue instead of overtaking it.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (relayCounter.Count < RebuildDrainRetryRelayHub.FailUntilRelayCount && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        relayCounter.Count.ShouldBeGreaterThanOrEqualTo(RebuildDrainRetryRelayHub.FailUntilRelayCount);

        await publisher.PublishMessage(new TransportMessage
        {
            MessageType = "TestCommand",
            SourceId = Guid.NewGuid(),
            Payload = "m3"
        });

        var completed = await Task.WhenAny(allReceived.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        completed.ShouldBe(allReceived.Task,
            "Expected all messages delivered in order after post-rebuild drain retry");
        lock (received)
        {
            received.ShouldBe(["m1", "m2", "m3"]);
        }
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
