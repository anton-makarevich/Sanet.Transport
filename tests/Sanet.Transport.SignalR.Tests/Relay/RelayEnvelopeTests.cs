using System.Text.Json;
using Sanet.Transport.SignalR.Client.Relay;
using Shouldly;
using Xunit;

namespace Sanet.Transport.SignalR.Tests.Relay;

public class RelayEnvelopeTests
{
    [Fact]
    public void TransportMessage_RoundTrips_ThroughRelayEnvelope()
    {
        // Arrange
        var originalMessage = new TransportMessage
        {
            MessageType = "MoveCommand",
            SourceId = Guid.NewGuid(),
            Payload = "{\"X\":10,\"Y\":20}",
            Timestamp = DateTime.UtcNow
        };

        var serializedPayload = JsonSerializer.Serialize(originalMessage);

        var envelope = new RelayEnvelope(
            SenderId: "conn-123",
            Payload: serializedPayload,
            SchemaVersion: "1.0.0",
            SequenceNumber: 42,
            Timestamp: DateTime.UtcNow);

        // Act
        var envelopeJson = JsonSerializer.Serialize(envelope);
        var deserializedEnvelope = JsonSerializer.Deserialize<RelayEnvelope>(envelopeJson);

        deserializedEnvelope.ShouldNotBeNull();
        var deserializedMessage = JsonSerializer.Deserialize<TransportMessage>(deserializedEnvelope.Payload);

        // Assert
        deserializedMessage.ShouldNotBeNull();
        deserializedMessage.MessageType.ShouldBe(originalMessage.MessageType);
        deserializedMessage.SourceId.ShouldBe(originalMessage.SourceId);
        deserializedMessage.Payload.ShouldBe(originalMessage.Payload);
    }

    [Fact]
    public void RelayEnvelope_Properties_InitializedCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var envelope = new RelayEnvelope("sender-1", "test-payload", "1.0.0", 100, now);

        // Assert
        envelope.SenderId.ShouldBe("sender-1");
        envelope.Payload.ShouldBe("test-payload");
        envelope.SchemaVersion.ShouldBe("1.0.0");
        envelope.SequenceNumber.ShouldBe(100);
        envelope.Timestamp.ShouldBe(now);
    }

    [Fact]
    public void HubError_Properties_InitializedCorrectly()
    {
        // Arrange
        var error = new HubError(HubErrorCode.HostDisconnected, "Host lost", "ROOM01");

        // Assert
        error.Code.ShouldBe(HubErrorCode.HostDisconnected);
        error.Message.ShouldBe("Host lost");
        error.RoomCode.ShouldBe("ROOM01");
    }
}
