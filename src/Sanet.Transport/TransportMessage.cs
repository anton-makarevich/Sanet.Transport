namespace Sanet.Transport;

/// <summary>
/// Represents a serializable transport message that can be sent between systems
/// without knowledge of the actual game command structure
/// </summary>
public record TransportMessage
{
    /// <summary>
    /// The type identifier of the command
    /// </summary>
    public required string MessageType { get; init; }
    
    /// <summary>
    /// The game origin identifier
    /// </summary>
    public required Guid SourceId { get; init; }
    
    /// <summary>
    /// Serialized payload of the command data
    /// </summary>
    public string Payload { get; set; } = string.Empty;
    
    /// <summary>
    /// When the command was created
    /// </summary>
    public DateTime Timestamp { get; set; }
}