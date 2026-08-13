using Microsoft.Extensions.Logging;
using Sanet.Transport.SignalR.Client.Publishers;

namespace Sanet.Transport.SignalR.Client.Factories;

/// <summary>
/// Creates <see cref="RelayClientPublisher"/> instances from <see cref="RelayPublisherOptions"/>.
/// The returned publisher is already connected to the hub.
/// </summary>
public sealed class RelayPublisherFactory : IPublisherFactory
{
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Creates a new instance of <see cref="RelayPublisherFactory"/>.
    /// </summary>
    /// <param name="loggerFactory">Logger factory used to create the relay publisher's logger.</param>
    public RelayPublisherFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public async Task<ITransportPublisher> Create(PublisherOptions options, CancellationToken cancellationToken = default)
    {
        if (options is not RelayPublisherOptions relayOptions)
        {
            throw new ArgumentException(
                $"Options must be of type {nameof(RelayPublisherOptions)}",
                nameof(options));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var logger = _loggerFactory.CreateLogger<RelayClientPublisher>();
        var publisher = new RelayClientPublisher(
            relayOptions.HubUrl,
            relayOptions.RoomCode,
            relayOptions.SessionToken,
            logger,
            relayOptions.ApiKey);

        logger.LogDebug(
            "Creating RelayClientPublisher for room {RoomCode} at {HubUrl}",
            relayOptions.RoomCode,
            relayOptions.HubUrl);

        // Start does not accept a token, so link one to abandon the publisher
        // promptly if the caller cancels while the connection is being established.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var startTask = publisher.StartAsync();
        try
        {
            var completed = await Task.WhenAny(startTask, Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token));
            if (completed != startTask)
            {
                linkedCts.Token.ThrowIfCancellationRequested();
            }

            await startTask;
            return publisher;
        }
        catch
        {
            try
            {
                await publisher.DisposeAsync();
            }
            catch
            {
                // Swallow to avoid masking the original failure
            }

            throw;
        }
    }
}
