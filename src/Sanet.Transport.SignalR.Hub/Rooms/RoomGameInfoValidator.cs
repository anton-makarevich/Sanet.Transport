using System.Text.RegularExpressions;
using Sanet.Transport.Relay.Contracts;

namespace Sanet.Transport.SignalR.Hub.Rooms;

/// <summary>
/// Single source of truth for <see cref="RoomGameInfo"/> validation. The REST controller turns
/// the returned field errors into a 400 <c>ValidationProblemDetails</c>; the room manager turns
/// them into an <see cref="ArgumentException"/>. Keys match the JSON path of the field.
/// </summary>
public static partial class RoomGameInfoValidator
{
    /// <summary>Field key used when the whole <c>gameInfo</c> object is missing.</summary>
    public const string GameInfoKey = nameof(CreateRoomRequest.GameInfo);

    /// <summary>Field key of the host game instance id inside <c>gameInfo</c>.</summary>
    public const string HostIdKey = GameInfoKey + "." + nameof(RoomGameInfo.HostId);

    /// <summary>Field key of the game title inside <c>gameInfo</c>.</summary>
    public const string IdKey = GameInfoKey + "." + nameof(RoomGameInfo.Id);

    /// <summary>Field key of the game version inside <c>gameInfo</c>.</summary>
    public const string VersionKey = GameInfoKey + "." + nameof(RoomGameInfo.Version);

    /// <summary>Field key of the game metadata inside <c>gameInfo</c>.</summary>
    public const string MetadataKey = GameInfoKey + "." + nameof(RoomGameInfo.Metadata);

    [GeneratedRegex(@"\A[A-Za-z0-9._-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex GameIdPattern();

    /// <summary>
    /// Validates <paramref name="gameInfo"/> against <see cref="RoomGameInfoLimits"/> and returns
    /// every field error keyed by its JSON path. An empty dictionary means the value is valid.
    /// </summary>
    /// <param name="gameInfo">Game identity and attributes supplied by the host; may be null.</param>
    public static Dictionary<string, string[]> Validate(RoomGameInfo? gameInfo)
    {
        var errors = new Dictionary<string, string[]>();

        if (gameInfo is null)
        {
            errors[GameInfoKey] = ["GameInfo is required."];
            return errors;
        }

        if (gameInfo.HostId == Guid.Empty)
        {
            errors[HostIdKey] = ["HostId must be a non-empty GUID."];
        }

        ValidateId(gameInfo.Id, errors);
        ValidateVersion(gameInfo.Version, errors);
        ValidateMetadata(gameInfo.Metadata, errors);

        return errors;
    }

    private static void ValidateId(string id, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            errors[IdKey] = ["Id is required."];
        }
        else if (id.Length > RoomGameInfoLimits.MaxIdLength)
        {
            errors[IdKey] = [$"Id must be at most {RoomGameInfoLimits.MaxIdLength} characters long."];
        }
        else if (!GameIdPattern().IsMatch(id))
        {
            errors[IdKey] = ["Id must contain only letters, digits, '.', '_' and '-'."];
        }
    }

    private static void ValidateVersion(string version, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            errors[VersionKey] = ["Version is required."];
        }
        else if (version.Length > RoomGameInfoLimits.MaxVersionLength)
        {
            errors[VersionKey] = [$"Version must be at most {RoomGameInfoLimits.MaxVersionLength} characters long."];
        }
        else if (!string.Equals(version, version.Trim(), StringComparison.Ordinal)
                 || version.Any(char.IsControl))
        {
            errors[VersionKey] = ["Version must not have leading or trailing whitespace or control characters."];
        }
    }

    private static void ValidateMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        Dictionary<string, string[]> errors)
    {
        if (metadata is null)
        {
            return;
        }

        if (metadata.Count > RoomGameInfoLimits.MaxMetadataEntries)
        {
            errors[MetadataKey] =
                [$"Metadata must contain at most {RoomGameInfoLimits.MaxMetadataEntries} entries."];
            return;
        }

        var metadataErrors = new List<string>();

        foreach (var (key, value) in metadata)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                metadataErrors.Add("Metadata keys must not be blank.");
            }
            else if (key.Length > RoomGameInfoLimits.MaxMetadataKeyLength)
            {
                metadataErrors.Add(
                    $"Metadata keys must be at most {RoomGameInfoLimits.MaxMetadataKeyLength} characters long.");
            }

            if (value is null)
            {
                metadataErrors.Add("Metadata values must not be null.");
            }
            else if (value.Length > RoomGameInfoLimits.MaxMetadataValueLength)
            {
                metadataErrors.Add(
                    $"Metadata values must be at most {RoomGameInfoLimits.MaxMetadataValueLength} characters long.");
            }
        }

        if (metadataErrors.Count > 0)
        {
            errors[MetadataKey] = [.. metadataErrors];
        }
    }
}
