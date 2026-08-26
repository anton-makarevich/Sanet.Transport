using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sanet.Transport.SignalR.Client.Factories;
using Sanet.Transport.SignalR.Client.Publishers;
using Sanet.Transport.SignalR.Tests.Publishers;
using Shouldly;
using Xunit;

namespace Sanet.Transport.SignalR.Tests.Factories;

public class RelayPublisherFactoryTests
{
    private const string RoomCode = "ABCDEF";
    private const string RelayTicket = "relay-ticket";

    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

    private readonly ILoggerFactory _loggerFactory = Substitute.For<ILoggerFactory>();
    private readonly RelayPublisherFactory _sut;

    public RelayPublisherFactoryTests()
    {
        _loggerFactory.CreateLogger<RelayClientPublisher>()
            .Returns(Substitute.For<ILogger<RelayClientPublisher>>());
        _sut = new RelayPublisherFactory(_loggerFactory);
    }

    private static RelayPublisherOptions Options(string hubUrl, string relayTicket = RelayTicket) => new()
    {
        HubUrl = hubUrl,
        RoomCode = RoomCode,
        RelayTicket = relayTicket
    };

    [Fact]
    public async Task Create_WhenOptionsAreWrongType_ThrowsArgumentException()
    {
        var wrongOptions = new NonRelayOptions();

        await Should.ThrowAsync<ArgumentException>(() => _sut.Create(wrongOptions));
    }

    [Fact]
    public async Task CreateAsync_WhenCancelledBeforeStart_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var exception = await Should.ThrowAsync<OperationCanceledException>(
            () => _sut.Create(Options("http://127.0.0.1:1/hubs/relay"), cts.Token));

        exception.CancellationToken.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task CreateAsync_WhenCancelledDuringConnection_ThrowsOperationCanceledException()
    {
        // A listener that accepts TCP connections but never completes the HTTP
        // handshake keeps the publisher's StartAsync pending while we cancel.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

            await Should.ThrowAsync<OperationCanceledException>(
                () => _sut.Create(Options($"http://127.0.0.1:{port}/hubs/relay"), cts.Token));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task CreateAsync_WhenHubUnreachable_Throws()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await WithTimeout(
            Should.ThrowAsync<Exception>(() => _sut.Create(Options($"http://127.0.0.1:{port}/hubs/relay"))));
    }

    [Fact]
    public async Task CreateAsync_WhenHubReachable_ReturnsConnectedPublisher()
    {
        await using var host = await TestRelayHubHost.StartAsync(RelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        var publisher = await WithTimeout(_sut.Create(Options(hubUrl)));

        await using var _ = publisher;
        publisher.ShouldBeOfType<RelayClientPublisher>().IsConnected.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateAsync_WhenHubRejectsWrongRelayTicket_Throws()
    {
        await using var host = await TestRelayHubHost.StartAsync(RelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        await WithTimeout(
            Should.ThrowAsync<Exception>(() => _sut.Create(Options(hubUrl, "wrong-ticket"))));
    }

    [Fact]
    public async Task CreateAsync_WhenConnectionFails_DisposesPublisher()
    {
        await using var host = await TestRelayHubHost.StartAsync(RelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        // A failed connection must dispose the partially-created publisher and rethrow;
        // the host must remain healthy for a subsequent valid connection.
        await WithTimeout(
            Should.ThrowAsync<Exception>(() => _sut.Create(Options(hubUrl, "wrong-ticket"))));

        var publisher = await WithTimeout(_sut.Create(Options(hubUrl)));

        await using var _ = publisher;
        publisher.ShouldBeOfType<RelayClientPublisher>().IsConnected.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateAsync_WithTicketRefresh_RebuildsConnectionAfterDropWithoutClosing()
    {
        const string refreshedTicket = "refreshed-relay-ticket";
        await using var host = await RebuildTestRelayHubHost.StartAsync(
            [RelayTicket, refreshedTicket],
            abortTicket: RelayTicket);
        var hubUrl = host.Urls.First().TrimEnd('/') + "/hubs/relay";

        var options = Options(hubUrl) with
        {
            TicketRefresh = _ => Task.FromResult<RelayTicketRefresh?>(
                new RelayTicketRefresh(refreshedTicket, DateTimeOffset.UtcNow.AddMinutes(5)))
        };

        var publisher = (RelayClientPublisher)await WithTimeout(_sut.Create(options));

        var reconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.Reconnected += _ => reconnected.TrySetResult(true);
        publisher.Closed += _ => closed.TrySetResult(true);

        var completed = await Task.WhenAny(reconnected.Task, closed.Task, Task.Delay(OperationTimeout));
        completed.ShouldBe(reconnected.Task, "Expected rebuild with fresh ticket after the forced drop");
        publisher.IsConnected.ShouldBeTrue();

        await publisher.DisposeAsync();
    }

    private static async Task<T> WithTimeout<T>(Task<T> task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(OperationTimeout));
        completed.ShouldBe(task, $"Operation did not complete within {OperationTimeout.TotalSeconds}s");
        return await task;
    }

    private sealed record NonRelayOptions : PublisherOptions;
}
