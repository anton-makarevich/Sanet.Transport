using Sanet.Transport.SignalR.Hub.Rooms;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests.Rooms;

public class CryptographicRoomCodeGeneratorTests
{
    [Fact]
    public void Generate_ReturnsSixUnambiguousCharacters()
    {
        var generator = new CryptographicRoomCodeGenerator();

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var roomCode = generator.Generate();

            roomCode.Length.ShouldBe(CryptographicRoomCodeGenerator.CodeLength);
            roomCode.All("ABCDEFGHJKMNPQRSTUVWXYZ23456789".Contains).ShouldBeTrue();
        }
    }
}
