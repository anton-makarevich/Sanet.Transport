using Sanet.Transport.Relay.Contracts;
using Sanet.Transport.SignalR.Hub.Rooms;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests.Rooms;

public class RoomGameInfoValidatorTests
{
    [Fact]
    public void Validate_ValidGameInfo_ReturnsNoErrors()
    {
        var gameInfo = new RoomGameInfo(
            Guid.NewGuid(),
            "MakaMek",
            "v0.64.0",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["rules"] = "standard" });

        var errors = RoomGameInfoValidator.Validate(gameInfo);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_NullGameInfo_ReportsGameInfoKey()
    {
        var errors = RoomGameInfoValidator.Validate(null);

        errors.ShouldContainKey(RoomGameInfoValidator.GameInfoKey);
        errors.ShouldHaveSingleItem();
    }

    [Fact]
    public void Validate_EmptyHostId_ReportsHostIdKey()
    {
        var errors = RoomGameInfoValidator.Validate(new RoomGameInfo(Guid.Empty, "MakaMek", "v0.64.0"));

        errors.ShouldContainKey(RoomGameInfoValidator.HostIdKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankId_ReportsIdKey(string id)
    {
        var errors = RoomGameInfoValidator.Validate(new RoomGameInfo(Guid.NewGuid(), id, "v0.64.0"));

        errors.ShouldContainKey(RoomGameInfoValidator.IdKey);
    }

    [Fact]
    public void Validate_TooLongId_ReportsIdKey()
    {
        var id = new string('a', RoomGameInfoLimits.MaxIdLength + 1);

        var errors = RoomGameInfoValidator.Validate(new RoomGameInfo(Guid.NewGuid(), id, "v0.64.0"));

        errors.ShouldContainKey(RoomGameInfoValidator.IdKey);
    }

    [Theory]
    [InlineData("not a valid id")]
    [InlineData("game/1")]
    [InlineData("game:id")]
    [InlineData("игра")]
    [InlineData("MakaMek\n")]
    public void Validate_IdWithUnsupportedCharacters_ReportsIdKey(string id)
    {
        var errors = RoomGameInfoValidator.Validate(new RoomGameInfo(Guid.NewGuid(), id, "v0.64.0"));

        errors.ShouldContainKey(RoomGameInfoValidator.IdKey);
    }

    [Theory]
    [InlineData("MakaMek")]
    [InlineData("game-1.2_3")]
    [InlineData("0123456789")]
    public void Validate_IdWithAllowedCharacters_IsAccepted(string id)
    {
        var errors = RoomGameInfoValidator.Validate(new RoomGameInfo(Guid.NewGuid(), id, "v0.64.0"));

        errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_BlankVersion_ReportsVersionKey(string version)
    {
        var errors = RoomGameInfoValidator.Validate(new RoomGameInfo(Guid.NewGuid(), "MakaMek", version));

        errors.ShouldContainKey(RoomGameInfoValidator.VersionKey);
    }

    [Fact]
    public void Validate_TooLongVersion_ReportsVersionKey()
    {
        var version = new string('v', RoomGameInfoLimits.MaxVersionLength + 1);

        var errors = RoomGameInfoValidator.Validate(new RoomGameInfo(Guid.NewGuid(), "MakaMek", version));

        errors.ShouldContainKey(RoomGameInfoValidator.VersionKey);
    }

    [Theory]
    [InlineData(" v0.64.0")]
    [InlineData("v0.64.0 ")]
    public void Validate_VersionWithSurroundingWhitespace_ReportsVersionKey(string version)
    {
        var errors = RoomGameInfoValidator.Validate(new RoomGameInfo(Guid.NewGuid(), "MakaMek", version));

        errors.ShouldContainKey(RoomGameInfoValidator.VersionKey);
    }

    [Fact]
    public void Validate_VersionWithControlCharacters_ReportsVersionKey()
    {
        var errors = RoomGameInfoValidator.Validate(new RoomGameInfo(Guid.NewGuid(), "MakaMek", "v0.64.0\n"));

        errors.ShouldContainKey(RoomGameInfoValidator.VersionKey);
    }

    [Fact]
    public void Validate_TooManyMetadataEntries_ReportsMetadataKey()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i <= RoomGameInfoLimits.MaxMetadataEntries; i++)
        {
            metadata[$"key{i}"] = "value";
        }

        var errors = RoomGameInfoValidator.Validate(
            new RoomGameInfo(Guid.NewGuid(), "MakaMek", "v0.64.0", metadata));

        errors.ShouldContainKey(RoomGameInfoValidator.MetadataKey);
    }

    [Fact]
    public void Validate_MaximumMetadataEntries_IsAccepted()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < RoomGameInfoLimits.MaxMetadataEntries; i++)
        {
            metadata[$"key{i}"] = "value";
        }

        var errors = RoomGameInfoValidator.Validate(
            new RoomGameInfo(Guid.NewGuid(), "MakaMek", "v0.64.0", metadata));

        errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankMetadataKey_ReportsMetadataKey(string key)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { [key] = "value" };

        var errors = RoomGameInfoValidator.Validate(
            new RoomGameInfo(Guid.NewGuid(), "MakaMek", "v0.64.0", metadata));

        errors.ShouldContainKey(RoomGameInfoValidator.MetadataKey);
    }

    [Fact]
    public void Validate_TooLongMetadataKey_ReportsMetadataKey()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [new string('k', RoomGameInfoLimits.MaxMetadataKeyLength + 1)] = "value"
        };

        var errors = RoomGameInfoValidator.Validate(
            new RoomGameInfo(Guid.NewGuid(), "MakaMek", "v0.64.0", metadata));

        errors.ShouldContainKey(RoomGameInfoValidator.MetadataKey);
    }

    [Fact]
    public void Validate_TooLongMetadataValue_ReportsMetadataKey()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rules"] = new string('v', RoomGameInfoLimits.MaxMetadataValueLength + 1)
        };

        var errors = RoomGameInfoValidator.Validate(
            new RoomGameInfo(Guid.NewGuid(), "MakaMek", "v0.64.0", metadata));

        errors.ShouldContainKey(RoomGameInfoValidator.MetadataKey);
    }

    [Fact]
    public void Validate_NullMetadataValue_ReportsMetadataKey()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["rules"] = null! };

        var errors = RoomGameInfoValidator.Validate(
            new RoomGameInfo(Guid.NewGuid(), "MakaMek", "v0.64.0", metadata));

        errors.ShouldContainKey(RoomGameInfoValidator.MetadataKey);
    }

    [Fact]
    public void Validate_InvalidMetadataKeyAndValue_ReportsEveryProblemUnderMetadataKey()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [" "] = new string('v', RoomGameInfoLimits.MaxMetadataValueLength + 1)
        };

        var errors = RoomGameInfoValidator.Validate(
            new RoomGameInfo(Guid.NewGuid(), "MakaMek", "v0.64.0", metadata));

        errors[RoomGameInfoValidator.MetadataKey].Length.ShouldBe(2);
    }

    [Fact]
    public void Validate_ReportsEveryInvalidFieldAtOnce()
    {
        var errors = RoomGameInfoValidator.Validate(
            new RoomGameInfo(
                Guid.Empty,
                "not a valid id",
                new string('v', RoomGameInfoLimits.MaxVersionLength + 1)));

        errors.Keys.ShouldBe(
        [
            RoomGameInfoValidator.HostIdKey,
            RoomGameInfoValidator.IdKey,
            RoomGameInfoValidator.VersionKey
        ], ignoreOrder: true);
    }

    [Fact]
    public void ValidateFilter_ValidFilter_ReturnsNoErrors()
    {
        var filter = new RoomListFilter(
            "MakaMek",
            "v0.64.0",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["rules"] = "standard" });

        var errors = RoomGameInfoValidator.ValidateFilter(filter);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateFilter_NullFilter_ReportsGameIdKey()
    {
        var errors = RoomGameInfoValidator.ValidateFilter(null);

        errors.ShouldContainKey(RoomGameInfoValidator.GameIdKey);
        errors.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateFilter_BlankGameId_ReportsGameIdKey(string gameId)
    {
        var errors = RoomGameInfoValidator.ValidateFilter(new RoomListFilter(gameId));

        errors.ShouldContainKey(RoomGameInfoValidator.GameIdKey);
    }

    [Fact]
    public void ValidateFilter_TooLongGameId_ReportsGameIdKey()
    {
        var gameId = new string('a', RoomGameInfoLimits.MaxIdLength + 1);

        var errors = RoomGameInfoValidator.ValidateFilter(new RoomListFilter(gameId));

        errors.ShouldContainKey(RoomGameInfoValidator.GameIdKey);
    }

    [Theory]
    [InlineData("not a valid id")]
    [InlineData("game/1")]
    [InlineData("игра")]
    public void ValidateFilter_GameIdWithUnsupportedCharacters_ReportsGameIdKey(string gameId)
    {
        var errors = RoomGameInfoValidator.ValidateFilter(new RoomListFilter(gameId));

        errors.ShouldContainKey(RoomGameInfoValidator.GameIdKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidateFilter_BlankVersion_ReportsVersionKey(string version)
    {
        var errors = RoomGameInfoValidator.ValidateFilter(
            new RoomListFilter("MakaMek", version));

        errors.ShouldContainKey(RoomGameInfoValidator.FilterVersionKey);
    }

    [Fact]
    public void ValidateFilter_TooLongVersion_ReportsVersionKey()
    {
        var version = new string('v', RoomGameInfoLimits.MaxVersionLength + 1);

        var errors = RoomGameInfoValidator.ValidateFilter(
            new RoomListFilter("MakaMek", version));

        errors.ShouldContainKey(RoomGameInfoValidator.FilterVersionKey);
    }

    [Theory]
    [InlineData(" v0.64.0")]
    [InlineData("v0.64.0 ")]
    public void ValidateFilter_VersionWithSurroundingWhitespace_ReportsVersionKey(string version)
    {
        var errors = RoomGameInfoValidator.ValidateFilter(
            new RoomListFilter("MakaMek", version));

        errors.ShouldContainKey(RoomGameInfoValidator.FilterVersionKey);
    }

    [Fact]
    public void ValidateFilter_TooManyMetadataEntries_ReportsMetadataKey()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i <= RoomGameInfoLimits.MaxMetadataEntries; i++)
        {
            metadata[$"key{i}"] = "value";
        }

        var errors = RoomGameInfoValidator.ValidateFilter(
            new RoomListFilter("MakaMek", Metadata: metadata));

        errors.ShouldContainKey(RoomGameInfoValidator.FilterMetadataKey);
    }

    [Fact]
    public void ValidateFilter_TooLongMetadataKey_ReportsFieldKeyedError()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [new string('k', RoomGameInfoLimits.MaxMetadataKeyLength + 1)] = "value"
        };

        var errors = RoomGameInfoValidator.ValidateFilter(
            new RoomListFilter("MakaMek", Metadata: metadata));

        errors.ShouldContainKey($"metadata[{metadata.Keys.Single()}]");
    }

    [Fact]
    public void ValidateFilter_TooLongMetadataValue_ReportsFieldKeyedError()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rules"] = new string('v', RoomGameInfoLimits.MaxMetadataValueLength + 1)
        };

        var errors = RoomGameInfoValidator.ValidateFilter(
            new RoomListFilter("MakaMek", Metadata: metadata));

        errors.ShouldContainKey("metadata[rules]");
    }

    [Fact]
    public void ValidateFilter_NullMetadataValue_ReportsFieldKeyedError()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["rules"] = null! };

        var errors = RoomGameInfoValidator.ValidateFilter(
            new RoomListFilter("MakaMek", Metadata: metadata));

        errors.ShouldContainKey("metadata[rules]");
    }

    [Fact]
    public void ValidateFilter_ReportsEveryInvalidFieldAtOnce()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rules"] = new string('v', RoomGameInfoLimits.MaxMetadataValueLength + 1)
        };
        var filter = new RoomListFilter(
            "not a valid id",
            new string('v', RoomGameInfoLimits.MaxVersionLength + 1),
            metadata);

        var errors = RoomGameInfoValidator.ValidateFilter(filter);

        errors.Keys.ShouldBe(
        [
            RoomGameInfoValidator.GameIdKey,
            RoomGameInfoValidator.FilterVersionKey,
            "metadata[rules]"
        ], ignoreOrder: true);
    }
}
