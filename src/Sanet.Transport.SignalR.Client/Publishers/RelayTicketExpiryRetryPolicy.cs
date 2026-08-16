using Microsoft.AspNetCore.SignalR.Client;

namespace Sanet.Transport.SignalR.Client.Publishers;

/// <summary>
/// <see cref="IRetryPolicy"/> that keeps automatic reconnect attempts inside the
/// validity window of the relay ticket bound into the connection URL. The relay hub
/// resolves unexpired tickets repeatedly, so a reconnect within the ticket window can
/// re-authenticate without requesting a fresh ticket. Retrying stops once the remaining
/// ticket validity drops below the retry-window margin, or when the next retry delay
/// would push the cumulative reconnect window past that point.
/// </summary>
internal sealed class RelayTicketExpiryRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8)
    ];

    private readonly DateTimeOffset _ticketExpiresAt;
    private readonly TimeSpan _retryWindowMargin;

    internal RelayTicketExpiryRetryPolicy(DateTimeOffset ticketExpiresAt)
        : this(ticketExpiresAt, TimeSpan.FromSeconds(2))
    {
    }

    internal RelayTicketExpiryRetryPolicy(DateTimeOffset ticketExpiresAt, TimeSpan retryWindowMargin)
    {
        _ticketExpiresAt = ticketExpiresAt;
        _retryWindowMargin = retryWindowMargin;
    }

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        var maxRetryWindow = _ticketExpiresAt - DateTimeOffset.UtcNow - _retryWindowMargin;
        if (maxRetryWindow <= TimeSpan.Zero)
        {
            return null;
        }

        var timeUntilWindowEnds = maxRetryWindow - retryContext.ElapsedTime;
        if (timeUntilWindowEnds <= TimeSpan.Zero)
        {
            return null;
        }

        var nextDelay = retryContext.PreviousRetryCount < RetryDelays.Length
            ? RetryDelays[retryContext.PreviousRetryCount]
            : RetryDelays[^1];

        return nextDelay <= timeUntilWindowEnds ? nextDelay : null;
    }
}
