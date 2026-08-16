using Sanet.Transport.SignalR.Hub.Rooms;
using Shouldly;

namespace Sanet.Transport.SignalR.Hub.Tests.Rooms;

public class RoomTests
{
    private static readonly DateTimeOffset DefaultNow = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(2);

    [Fact]
    public void Constructor_SetsExpiresAtFromProvidedValue()
    {
        var hostGameId = Guid.NewGuid();
        var expiresAt = DefaultNow.Add(DefaultTtl);

        var room = CreateRoom(Guid.NewGuid(), hostGameId);

        room.CreatedAt.ShouldBe(DefaultNow);
        room.ExpiresAt.ShouldBe(expiresAt);
    }

    [Fact]
    public void Constructor_SetsHostGameIdFromCreateRequest()
    {
        var hostGameId = Guid.NewGuid();

        var room = CreateRoom(Guid.NewGuid(), hostGameId);

        room.HostGameId.ShouldBe(hostGameId);
        // HostGameId is game identity, not a membership or connection key.
        room.Members.Single().DeviceSessionId.ShouldNotBe(hostGameId);
        room.LiveConnectionCount.ShouldBe(0);
    }

    [Fact]
    public void RemoveMember_HostDeviceSession_ReturnsFalse()
    {
        var hostDeviceSessionId = Guid.NewGuid();
        var room = CreateRoom(hostDeviceSessionId, Guid.NewGuid());

        var result = room.RemoveMember(hostDeviceSessionId);

        result.ShouldBeFalse();
        room.IsMember(hostDeviceSessionId).ShouldBeTrue();
        room.Members.Count.ShouldBe(1);
    }

    [Fact]
    public void RemoveMember_NonMember_ReturnsFalse()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());

        var result = room.RemoveMember(Guid.NewGuid());

        result.ShouldBeFalse();
        room.Members.Count.ShouldBe(1);
    }

    [Fact]
    public void RemoveMember_ClientMember_RemovesMemberAndRevokesSessions()
    {
        var hostDeviceSessionId = Guid.NewGuid();
        var clientDeviceSessionId = Guid.NewGuid();
        var room = CreateRoom(hostDeviceSessionId, Guid.NewGuid());
        var clientSession1 = room.AddClientMember(clientDeviceSessionId, DefaultNow, DefaultTtl, () => "client-token-1");
        var clientSession2 = room.AddClientMember(clientDeviceSessionId, DefaultNow, DefaultTtl, () => "client-token-2");

        var result = room.RemoveMember(clientDeviceSessionId);

        result.ShouldBeTrue();
        room.IsMember(clientDeviceSessionId).ShouldBeFalse();
        room.HasSession(clientSession1.Token).ShouldBeFalse();
        room.HasSession(clientSession2.Token).ShouldBeFalse();
        room.Members.Count.ShouldBe(1);
        room.IsMember(hostDeviceSessionId).ShouldBeTrue();
    }

    [Fact]
    public void TryGetSession_WithMismatchedRoomCodeInSession_ReturnsSessionWithDifferentCode()
    {
        var hostDeviceSessionId = Guid.NewGuid();
        var hostMember = new RoomMember(hostDeviceSessionId, RoomRole.Host, DefaultNow);
        var hostSession = new RoomSession("host-token", "WRONG", hostDeviceSessionId, RoomRole.Host, DefaultNow.Add(DefaultTtl));
        var room = new Room("ABC234", Guid.NewGuid(), hostMember, hostSession, DefaultNow, DefaultNow.Add(DefaultTtl));

        var found = room.TryGetSession("host-token", out var session);

        found.ShouldBeTrue();
        session.RoomCode.ShouldBe("WRONG");
        session.RoomCode.ShouldNotBe(room.RoomCode);
    }

    [Fact]
    public void RegisterConnection_ForDeviceSession_ReturnsReplacedConnectionAndTouchesRoom()
    {
        var deviceSessionId = Guid.NewGuid();
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());

        room.RegisterConnection(deviceSessionId, "old", DefaultNow, DefaultTtl).ShouldBeNull();
        var replaced = room.RegisterConnection(deviceSessionId, "new", DefaultNow.AddMinutes(5), DefaultTtl);

        replaced.ShouldBe("old");
        room.GetConnectionId(deviceSessionId).ShouldBe("new");
        room.LiveConnectionCount.ShouldBe(1);
        room.LastActivityAt.ShouldBe(DefaultNow.AddMinutes(5));
        room.ExpiresAt.ShouldBe(DefaultNow.AddHours(2).AddMinutes(5));
    }

    [Fact]
    public void RemoveConnection_OnlyRemovesActiveConnection()
    {
        var deviceSessionId = Guid.NewGuid();
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());
        room.RegisterConnection(deviceSessionId, "new", DefaultNow, DefaultTtl);

        room.RemoveConnection(deviceSessionId, "old", DefaultNow.AddMinutes(1), DefaultTtl).ShouldBeFalse();
        room.GetConnectionId(deviceSessionId).ShouldBe("new");
        room.RemoveConnection(deviceSessionId, "new", DefaultNow.AddMinutes(2), DefaultTtl).ShouldBeTrue();

        room.GetConnectionId(deviceSessionId).ShouldBeNull();
        room.LiveConnectionCount.ShouldBe(0);
        room.LastActivityAt.ShouldBe(DefaultNow.AddMinutes(2));
    }

    [Fact]
    public void Reconnect_ReusesDeviceSessionAndRemapsConnection()
    {
        var hostDeviceSessionId = Guid.NewGuid();
        var room = CreateRoom(hostDeviceSessionId, Guid.NewGuid());

        // First connection for the host device, then a reconnect: same device
        // session identity, superseded ConnectionId, new active ConnectionId.
        room.RegisterConnection(hostDeviceSessionId, "host-old", DefaultNow, DefaultTtl);
        var replaced = room.RegisterConnection(hostDeviceSessionId, "host-new", DefaultNow.AddMinutes(1), DefaultTtl);

        replaced.ShouldBe("host-old");
        room.GetConnectionId(hostDeviceSessionId).ShouldBe("host-new");
        room.GetHostConnectionId().ShouldBe("host-new");
        room.LiveConnectionCount.ShouldBe(1);
    }

    [Fact]
    public void Dissolution_CanBeMarkedCancelledAndDetectedAtDeadline()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());
        var grace = TimeSpan.FromSeconds(30);

        room.MarkForDissolution(DefaultNow, grace);

        room.IsDissolving.ShouldBeTrue();
        room.IsDissolvedAt(DefaultNow.AddSeconds(29)).ShouldBeFalse();
        room.IsDissolvedAt(DefaultNow.AddSeconds(30)).ShouldBeTrue();
        room.State.ShouldBe(RoomState.Created);

        room.CancelDissolution();
        room.IsDissolving.ShouldBeFalse();
        room.IsDissolvedAt(DefaultNow.AddMinutes(1)).ShouldBeFalse();
    }

    [Fact]
    public void RevokeAllSessions_RevokesHostAndClientSessions()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());
        var client = room.AddClientMember(Guid.NewGuid(), DefaultNow, DefaultTtl, () => "client-token");

        room.RevokeAllSessions();

        room.HasSession("host-token").ShouldBeFalse();
        room.HasSession(client.Token).ShouldBeFalse();
    }

    [Fact]
    public void AddClientMember_HostDeviceSession_ThrowsAndLeavesHostValid()
    {
        var hostDeviceSessionId = Guid.NewGuid();
        var room = CreateRoom(hostDeviceSessionId, Guid.NewGuid());

        var ex = Should.Throw<InvalidOperationException>(() =>
            room.AddClientMember(hostDeviceSessionId, DefaultNow, DefaultTtl, () => "client-token"));
        ex.Message.ShouldContain("host device session");

        room.Members.Count.ShouldBe(1);
        room.HasSession("host-token").ShouldBeTrue();
    }

    [Fact]
    public void ValidateMemberSession_ClientTokenForOwnDevice_ReturnsTrue()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());
        var client = room.AddClientMember(Guid.NewGuid(), DefaultNow, DefaultTtl, () => "client-token");

        room.ValidateMemberSession(client.Token, client.DeviceSessionId, DefaultNow).ShouldBeTrue();
    }

    [Fact]
    public void ValidateMemberSession_ClientTokenForOtherDevice_ReturnsFalse()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());
        var client = room.AddClientMember(Guid.NewGuid(), DefaultNow, DefaultTtl, () => "client-token");
        var otherDevice = Guid.NewGuid();

        room.ValidateMemberSession(client.Token, otherDevice, DefaultNow).ShouldBeFalse();
    }

    [Fact]
    public void ValidateMemberSession_HostToken_ReturnsFalse()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());

        room.ValidateMemberSession("host-token", room.HostDeviceSessionId, DefaultNow).ShouldBeFalse();
    }

    [Fact]
    public void ValidateMemberSession_ExpiredClientToken_ReturnsFalse()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());
        var client = room.AddClientMember(Guid.NewGuid(), DefaultNow, DefaultTtl, () => "client-token");

        var expiredAt = DefaultNow.Add(DefaultTtl).Add(TimeSpan.FromMinutes(1));

        room.ValidateMemberSession(client.Token, client.DeviceSessionId, expiredAt).ShouldBeFalse();
    }

    [Fact]
    public void IssueRelayTicket_WithLiveSession_StoresResolvableTicket()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());

        var issued = room.IssueRelayTicket("host-token", "ticket-1", DefaultNow, TimeSpan.FromSeconds(60));

        issued.ShouldBeTrue();
        room.TryResolveRelayTicket("ticket-1", DefaultNow, out var session).ShouldBeTrue();
        session.Token.ShouldBe("host-token");
    }

    [Fact]
    public void IssueRelayTicket_WithUnknownSession_ReturnsFalse()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());

        var issued = room.IssueRelayTicket("no-such-token", "ticket-1", DefaultNow, TimeSpan.FromSeconds(60));

        issued.ShouldBeFalse();
    }

    [Fact]
    public void IssueRelayTicket_WithExpiredSession_ReturnsFalse()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());

        var issued = room.IssueRelayTicket(
            "host-token",
            "ticket-1",
            DefaultNow.AddHours(3),
            TimeSpan.FromSeconds(60));

        issued.ShouldBeFalse();
    }

    [Fact]
    public void TryResolveRelayTicket_WithUnknownTicket_ReturnsFalse()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());

        room.TryResolveRelayTicket("no-such-ticket", DefaultNow, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryResolveRelayTicket_AfterTicketExpiry_ReturnsFalse()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());
        room.IssueRelayTicket("host-token", "ticket-1", DefaultNow, TimeSpan.FromSeconds(60)).ShouldBeTrue();

        room.TryResolveRelayTicket("ticket-1", DefaultNow.AddSeconds(61), out _).ShouldBeFalse();
    }

    [Fact]
    public void TryResolveRelayTicket_BeforeTicketExpiry_ResolvesRepeatedly()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());
        room.IssueRelayTicket("host-token", "ticket-1", DefaultNow, TimeSpan.FromSeconds(60)).ShouldBeTrue();

        room.TryResolveRelayTicket("ticket-1", DefaultNow.AddSeconds(30), out _).ShouldBeTrue();
        room.TryResolveRelayTicket("ticket-1", DefaultNow.AddSeconds(59), out _).ShouldBeTrue();
    }

    [Fact]
    public void TryResolveRelayTicket_AfterSessionRevoked_ReturnsFalse()
    {
        var clientDeviceSessionId = Guid.NewGuid();
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());
        var client = room.AddClientMember(clientDeviceSessionId, DefaultNow, DefaultTtl, () => "client-token");
        room.IssueRelayTicket(client.Token, "ticket-1", DefaultNow, TimeSpan.FromSeconds(60)).ShouldBeTrue();

        room.RemoveMember(clientDeviceSessionId);

        room.TryResolveRelayTicket("ticket-1", DefaultNow, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryResolveRelayTicket_WhenRoomExpired_ReturnsFalse()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());
        room.IssueRelayTicket("host-token", "ticket-1", DefaultNow, TimeSpan.FromHours(5)).ShouldBeTrue();

        room.TryResolveRelayTicket("ticket-1", DefaultNow.AddHours(3), out _).ShouldBeFalse();
    }

    [Fact]
    public void IssueRelayTicket_WithExpiredTicketsPresent_PrunesThemBeforeIssuing()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());
        room.IssueRelayTicket("host-token", "expired-ticket", DefaultNow, TimeSpan.FromSeconds(60)).ShouldBeTrue();
        var later = DefaultNow.AddSeconds(61);

        for (var i = 0; i < Room.MaxActiveRelayTickets; i++)
        {
            room.IssueRelayTicket("host-token", $"live-{i}", later, TimeSpan.FromSeconds(60)).ShouldBeTrue();
        }
    }

    [Fact]
    public void TryResolveRelayTicket_RemovesExpiredTicketsAsLookupSideEffect()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());
        room.IssueRelayTicket("host-token", "expired-ticket", DefaultNow, TimeSpan.FromSeconds(60)).ShouldBeTrue();
        var later = DefaultNow.AddSeconds(61);

        room.TryResolveRelayTicket("expired-ticket", later, out _).ShouldBeFalse();

        for (var i = 0; i < Room.MaxActiveRelayTickets; i++)
        {
            room.IssueRelayTicket("host-token", $"live-{i}", later, TimeSpan.FromSeconds(60)).ShouldBeTrue();
        }
    }

    [Fact]
    public void IssueRelayTicket_AtCapacityWithAllLiveTickets_ReturnsFalse()
    {
        var room = CreateRoom(Guid.NewGuid(), Guid.NewGuid());

        for (var i = 0; i < Room.MaxActiveRelayTickets; i++)
        {
            room.IssueRelayTicket("host-token", $"live-{i}", DefaultNow, TimeSpan.FromSeconds(60)).ShouldBeTrue();
        }

        room.IssueRelayTicket("host-token", "overflow", DefaultNow, TimeSpan.FromSeconds(60)).ShouldBeFalse();
    }

    private static Room CreateRoom(Guid hostDeviceSessionId, Guid hostGameId)
    {
        var hostMember = new RoomMember(hostDeviceSessionId, RoomRole.Host, DefaultNow);
        var hostSession = new RoomSession("host-token", "ABC234", hostDeviceSessionId, RoomRole.Host, DefaultNow.Add(DefaultTtl));
        return new Room("ABC234", hostGameId, hostMember, hostSession, DefaultNow, DefaultNow.Add(DefaultTtl));
    }
}
