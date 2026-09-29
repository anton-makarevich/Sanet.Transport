namespace Sanet.Transport.Relay.Contracts;

/// <summary>
/// Fixed bounds applied to <see cref="RoomGameInfo"/> members at the Hub boundary. The Hub
/// validator, the client documentation, and the tests all reference these values so the limits
/// never drift apart.
/// </summary>
public static class RoomGameInfoLimits
{
    /// <summary>
    /// Maximum number of characters in <see cref="RoomGameInfo.Id"/>. The value must also use
    /// only letters, digits, <c>'.'</c>, <c>'_'</c> and <c>'-'</c> so it stays safe for
    /// query-string filtering.
    /// </summary>
    public const int MaxIdLength = 64;

    /// <summary>
    /// Maximum number of characters in <see cref="RoomGameInfo.Version"/>. The value must not be
    /// blank, must not have leading or trailing whitespace, and must not contain control
    /// characters.
    /// </summary>
    public const int MaxVersionLength = 32;

    /// <summary>
    /// Maximum number of entries in <see cref="RoomGameInfo.Metadata"/>. Keys must not be blank
    /// and are compared with ordinal (case-sensitive) semantics.
    /// </summary>
    public const int MaxMetadataEntries = 32;

    /// <summary>
    /// Maximum number of characters in a <see cref="RoomGameInfo.Metadata"/> key.
    /// </summary>
    public const int MaxMetadataKeyLength = 64;

    /// <summary>
    /// Maximum number of characters in a <see cref="RoomGameInfo.Metadata"/> value.
    /// </summary>
    public const int MaxMetadataValueLength = 512;
}
