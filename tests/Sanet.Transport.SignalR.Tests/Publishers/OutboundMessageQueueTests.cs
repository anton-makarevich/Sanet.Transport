using Microsoft.Extensions.Logging;
using NSubstitute;
using Sanet.Transport.SignalR.Client.Publishers;
using Shouldly;
using Xunit;

namespace Sanet.Transport.SignalR.Tests.Publishers;

public class OutboundMessageQueueTests
{
    private readonly ILogger _logger = Substitute.For<ILogger>();
    private readonly OutboundMessageQueue _sut = new(3);

    private static TransportMessage MakeMessage(string payload) => new()
    {
        MessageType = "TestCommand",
        SourceId = Guid.NewGuid(),
        Payload = payload
    };

    [Fact]
    public void Constructor_WithValidCapacity_SetsCapacity()
    {
        var sut = new OutboundMessageQueue(7);

        sut.Capacity.ShouldBe(7);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveCapacity_ThrowsArgumentOutOfRange(int capacity)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new OutboundMessageQueue(capacity));
    }

    [Fact]
    public void Count_WhenEmpty_IsZero()
    {
        _sut.Count.ShouldBe(0);
    }

    [Fact]
    public void EnqueueOrThrow_BelowCapacity_AddsMessagesAndUpdatesCount()
    {
        // Act
        _sut.EnqueueOrThrow(MakeMessage("m1"), _logger);
        _sut.EnqueueOrThrow(MakeMessage("m2"), _logger);

        // Assert
        _sut.Count.ShouldBe(2);
    }

    [Fact]
    public void EnqueueOrThrow_WhenQueueIsFull_ThrowsTransportPublishExceptionWithQueueFull()
    {
        // Arrange
        foreach (var payload in new[] { "m1", "m2", "m3" })
        {
            _sut.EnqueueOrThrow(MakeMessage(payload), _logger);
        }

        // Act & Assert
        var exception = Should.Throw<TransportPublishException>(
            () => _sut.EnqueueOrThrow(MakeMessage("m4"), _logger));
        exception.Reason.ShouldBe(PublishFailureReason.QueueFull);

        // The rejected message must not be stored.
        _sut.Count.ShouldBe(3);
    }

    [Fact]
    public void TryDequeue_WithQueuedMessages_ReturnsMessagesInFifoOrder()
    {
        // Arrange
        var m1 = MakeMessage("m1");
        var m2 = MakeMessage("m2");
        _sut.EnqueueOrThrow(m1, _logger);
        _sut.EnqueueOrThrow(m2, _logger);

        // Act & Assert
        _sut.TryDequeue().ShouldBe(m1);
        _sut.TryDequeue().ShouldBe(m2);
        _sut.Count.ShouldBe(0);
    }

    [Fact]
    public void TryDequeue_WhenEmpty_ReturnsNull()
    {
        _sut.TryDequeue().ShouldBeNull();
    }

    [Fact]
    public void RequeueAhead_OnFailedFlush_PutsMessageAheadOfMessagesEnqueuedMidFlush()
    {
        // Arrange: m1 dequeued and fails to send; m2 was enqueued mid-flush.
        var m1 = MakeMessage("failed");
        var m2 = MakeMessage("mid-flush");
        _sut.EnqueueOrThrow(m2, _logger);

        // Act
        _sut.RequeueAhead(m1);

        // Assert
        _sut.Count.ShouldBe(2);
        _sut.TryDequeue().ShouldBe(m1);
        _sut.TryDequeue().ShouldBe(m2);
    }

    [Fact]
    public void RequeueAhead_PreservesOrderOfAllRemainingMessages()
    {
        // Arrange
        var failed = MakeMessage("failed");
        var remaining1 = MakeMessage("r1");
        var remaining2 = MakeMessage("r2");
        _sut.EnqueueOrThrow(remaining1, _logger);
        _sut.EnqueueOrThrow(remaining2, _logger);

        // Act
        _sut.RequeueAhead(failed);

        // Assert
        _sut.TryDequeue().ShouldBe(failed);
        _sut.TryDequeue().ShouldBe(remaining1);
        _sut.TryDequeue().ShouldBe(remaining2);
    }

    [Fact]
    public void RequeueAhead_WhenQueueIsEmpty_MakesMessageTheOnlyItem()
    {
        // Arrange
        var message = MakeMessage("only");

        // Act
        _sut.RequeueAhead(message);

        // Assert
        _sut.Count.ShouldBe(1);
        _sut.TryDequeue().ShouldBe(message);
    }
}
