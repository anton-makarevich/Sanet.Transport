using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Sanet.Transport.SignalR.Client.Relay;

namespace Sanet.Transport.SignalR.Client.Publishers;

/// <summary>
/// Relay-specific implementation of <see cref="ITransportPublisher"/> using SignalR.
/// <para>
/// Connects outbound to a cloud RelayHub using WebSockets and short-lived relay-ticket
/// authentication. The relay ticket is bound into the connection URL at construction time.
/// When <c>relayTicketExpiresAt</c> is supplied, automatic reconnect is configured
/// with a retry window that ends before the ticket expires, so repeatable unexpired tickets are
/// reused after transient transport failures. When <c>ticketRefresh</c> is supplied,
/// a closed connection is not terminal: the delegate is invoked to obtain a fresh relay ticket,
/// the underlying <see cref="HubConnection"/> is rebuilt around it (preserving subscribers and
/// public events) and restarted.
/// </para>
/// <para>
/// Concurrency model: a single lifecycle actor owns connection identity and recovery, and a
/// single outbound pump is the only code that ever sends messages. Every message published via
/// <see cref="PublishMessage"/> enters one bounded pipeline; while the connection is recovering,
/// the pump pauses and messages accumulate (bounded by <c>outboundQueueCapacity</c>),
/// resuming in FIFO order once connectivity returns — there is no separate "queued vs direct"
/// send path. The task returned by <see cref="PublishMessage"/> completes when the message has
/// actually been sent (or definitively failed), so awaiting callers get truthful backpressure
/// across recovery windows.
/// </para>
/// <para>
/// The public <see cref="Closed"/> event fires only when no recovery path exists (neither a
/// ticket-expiry reconnect window nor a refresh delegate), when the refresh delegate fails or
/// returns null, or when the bounded restart attempts are exhausted. Once closed, callers must
/// obtain a fresh relay ticket and recreate this publisher.
/// </para>
/// <para>
/// Subscriber callbacks and public events are dispatched asynchronously off the raising thread,
/// via the <see cref="SynchronizationContext"/> active at construction time if any. Consumers on
/// UI frameworks (Avalonia, WPF, WinUI) should construct this publisher on the UI thread to
/// receive callbacks without manual marshaling. Event handlers may safely call
/// <see cref="PublishMessage"/> reentrantly; reentrant <see cref="StartAsync"/> calls from event
/// handlers are not supported (the documented contract after <see cref="Closed"/> is to recreate
/// the publisher).
/// </para>
/// </summary>
public class RelayClientPublisher : ITransportPublisher
{
    private const int MaxRestartAttempts = 3;
    private const int MaxSendRetryFailures = 10;
    private const int DefaultOutboundQueueCapacity = 500;
    private static readonly TimeSpan SendRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RestartRetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly string _hubUrl;
    private readonly string _roomCode;
    private readonly ILogger<RelayClientPublisher> _logger;
    private readonly SynchronizationContext? _syncContext;
    private readonly List<Action<TransportMessage>> _subscribers = [];
    private readonly Func<CancellationToken, Task<RelayTicketRefresh?>>? _ticketRefresh;
    private readonly bool _hasRecoveryPath;
    private readonly CancellationTokenSource _lifetimeCts = new();

    // Lifecycle commands: the single mailbox of the lifecycle actor. Every external entry point
    // (StartAsync) and every SignalR connection callback posts here instead of touching state.
    private readonly Channel<LifecycleCommand> _commands = Channel.CreateUnbounded<LifecycleCommand>();

    // Unified outbound pipeline: every published message enters this bounded channel and the
    // pump is the only sender. Capacity IS the QueueFull backpressure point.
    private readonly Channel<PendingSend> _outbound;
    private readonly int _outboundCapacity;

    // Coalescing resume signal for the pump (at most one pending wake; redundant writes drop).
    // Three wake conditions: enable (send backlog), terminal close (fault backlog), disposal.
    private readonly Channel<byte> _resumeSignal = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    // Published by the lifecycle actor only; readable from any thread without a lock.
    private volatile ConnectionSnapshot _snapshot;

    private readonly Task _lifecycleTask;
    private readonly Task _pumpTask;

    // In-flight background operations tracked so disposal can await them before touching the
    // published connection (a rebuild may have created a replacement that is not in _snapshot yet).
    private volatile Task? _activeStartTask;
    private volatile Task? _activeRebuildTask;

    private long _sequenceNumber;
    private volatile bool _isDisposed;
    private int _terminalClosedRaised;
    private long _transitionSequence;

    // Closed-handler bookkeeping per connection instance. Mostly actor-owned, but the
    // rebuild background task attaches/detaches too, so dictionary access stays guarded.
    private readonly Lock _closedHandlersGate = new();
    private readonly Dictionary<HubConnection, Func<Exception?, Task>> _closedHandlers = new();

    // Preserves FIFO delivery when a SynchronizationContext is captured. Contexts
    // such as xUnit's AsyncTestSyncContext delegate Post to the thread pool, which
    // gives no execution-order guarantee; queueing notifications and draining them
    // with a single in-flight post keeps messages ordered for subscribers. The queue
    // is intentionally unbounded (unlike _outbound): dropping notifications would
    // silently violate delivery, and inbound match traffic is low-volume.
    private readonly Lock _dispatchLock = new();
    private readonly Queue<(TransportMessage Message, Action<TransportMessage>[] Subscribers)> _pendingNotifications = new();
    private bool _dispatchPending;

    /// <summary>
    /// Immutable view of the connection, published by the lifecycle actor on every transition.
    /// <see cref="SendEnabled"/> gates the outbound pump; <see cref="HasRecoveryPath"/> distinguishes
    /// a temporary pause (messages queue) from a non-recoverable disconnect (publishes reject with
    /// <see cref="PublishFailureReason.NotConnected"/>); <see cref="TerminallyClosed"/> makes the
    /// pump fault everything pending instead of holding it forever.
    /// </summary>
    private sealed record ConnectionSnapshot(
        HubConnection Connection,
        bool SendEnabled,
        bool HasRecoveryPath,
        bool TerminallyClosed);

    private sealed record PendingSend(TransportMessage Message, TaskCompletionSource<object?> Completion);

    private abstract record LifecycleCommand
    {
        private LifecycleCommand() { }

        public sealed record Start(TaskCompletionSource<object?> Completion) : LifecycleCommand;

        public sealed record StartCompleted(TaskCompletionSource<object?> Completion, Exception? Error)
            : LifecycleCommand;

        // Sequence stamped at post time: SignalR may raise the Reconnecting and Reconnected
        // callbacks concurrently, so the actor applies whichever transition was announced
        // LAST and drops stale ones that arrive out of order.
        public sealed record HubReconnecting(long Sequence, Exception? Error) : LifecycleCommand;

        public sealed record HubReconnected(long Sequence, string? ConnectionId) : LifecycleCommand;

        public sealed record HubClosed(HubConnection Connection, Exception? Error) : LifecycleCommand;

        public sealed record RebuildCompleted(HubConnection? NewConnection, Exception? Error) : LifecycleCommand;
    }

    /// <summary>
    /// Event raised when a peer connects to the room.
    /// </summary>
    public event Action<string>? PeerConnected;

    /// <summary>
    /// Event raised when a peer disconnects from the room.
    /// </summary>
    public event Action<string>? PeerDisconnected;

    /// <summary>
    /// Event raised when a hub error is received.
    /// </summary>
    public event Action<HubError>? HubErrorReceived;

    /// <summary>
    /// Event raised when the host disconnects from the room.
    /// </summary>
    public event Action? HostDisconnected;

    /// <summary>
    /// Event raised when the connection is attempting to reconnect.
    /// </summary>
    public event Action<Exception?>? Reconnecting;

    /// <summary>
    /// Event raised when the connection has been reestablished, either by automatic
    /// reconnect within a ticket window or by a successful rebuild with a freshly
    /// issued relay ticket.
    /// </summary>
    public event Action<string?>? Reconnected;

    /// <summary>
    /// Event raised when the connection has been closed terminally. This fires when no
    /// recovery path exists (no ticket expiry window and no refresh delegate), when the
    /// refresh delegate fails or returns null, when the bounded restart attempts are
    /// exhausted, or when the publisher is disposed. Once closed, callers must obtain a
    /// fresh relay ticket and recreate this publisher.
    /// </summary>
    public event Action<Exception?>? Closed;

    /// <summary>
    /// Gets the current state of the underlying SignalR connection.
    /// </summary>
    public HubConnectionState State => _snapshot.Connection.State;

    /// <summary>
    /// Gets whether the publisher is currently connected to the hub.
    /// </summary>
    public bool IsConnected => _snapshot.Connection.State == HubConnectionState.Connected;

    /// <summary>
    /// Creates a new instance of <see cref="RelayClientPublisher"/>.
    /// </summary>
    /// <param name="hubUrl">The base URL of the SignalR relay hub.</param>
    /// <param name="roomCode">The 6-character room code.</param>
    /// <param name="relayTicket">The short-lived relay ticket issued by the REST relay-ticket API.</param>
    /// <param name="logger">Logger</param>
    /// <param name="relayTicketExpiresAt">
    /// When provided, enables automatic reconnect with a retry window that ends before this
    /// expiry, reusing the repeatable ticket after transient transport failures. When null,
    /// no automatic reconnect is configured.
    /// </param>
    /// <param name="ticketRefresh">
    /// When provided, invoked after the connection closes to obtain a fresh relay ticket
    /// and transparently rebuild and restart the underlying connection. Return null or throw
    /// to make the close terminal (raising <see cref="Closed"/>). The cancellation token is
    /// canceled when the publisher is disposed.
    /// </param>
    /// <param name="outboundQueueCapacity">
    /// The maximum number of messages held in the outbound pipeline while the connection is
    /// recovering. When full, <see cref="PublishMessage"/> throws
    /// <see cref="TransportPublishException"/> with <see cref="PublishFailureReason.QueueFull"/>.
    /// </param>
    public RelayClientPublisher(
        string hubUrl,
        string roomCode,
        string relayTicket,
        ILogger<RelayClientPublisher> logger,
        DateTimeOffset? relayTicketExpiresAt = null,
        Func<CancellationToken, Task<RelayTicketRefresh?>>? ticketRefresh = null,
        int outboundQueueCapacity = DefaultOutboundQueueCapacity)
    {
        _logger = logger;
        _syncContext = SynchronizationContext.Current;

        if (string.IsNullOrWhiteSpace(hubUrl))
        {
            throw new ArgumentException("Hub URL cannot be null or empty", nameof(hubUrl));
        }

        if (roomCode is null || roomCode.Length != 6)
        {
            throw new ArgumentException("Room code must be exactly 6 characters", nameof(roomCode));
        }

        if (string.IsNullOrWhiteSpace(relayTicket))
        {
            throw new ArgumentException("Relay ticket cannot be null or empty", nameof(relayTicket));
        }

        if (outboundQueueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outboundQueueCapacity),
                "Outbound queue capacity must be greater than zero.");
        }

        _hubUrl = hubUrl;
        _roomCode = roomCode;
        _ticketRefresh = ticketRefresh;
        _hasRecoveryPath = ticketRefresh is not null || relayTicketExpiresAt.HasValue;
        _outboundCapacity = outboundQueueCapacity;

        var connection = BuildConnection(hubUrl, relayTicket, relayTicketExpiresAt);
        AttachHandlers(connection);

        // Pump starts parked: nothing may be sent before StartAsync succeeds.
        _snapshot = new ConnectionSnapshot(connection, SendEnabled: false, _hasRecoveryPath, TerminallyClosed: false);
        _outbound = Channel.CreateBounded<PendingSend>(
            new BoundedChannelOptions(outboundQueueCapacity) { SingleReader = true, SingleWriter = false });

        // Both loops run for the publisher's lifetime; disposal cancels the token, completes
        // the channels, awaits both tasks and faults anything still pending.
        _pumpTask = Task.Run(RunOutboundPumpAsync);
        _lifecycleTask = Task.Run(RunLifecycleLoopAsync);
    }

    /// <summary>
    /// Builds the SignalR hub connection URL, appending the relay ticket as a query-string
    /// parameter and replacing any ticket parameter already present in the hub URL.
    /// </summary>
    /// <param name="hubUrl">The base URL of the SignalR relay hub.</param>
    /// <param name="relayTicket">The short-lived relay ticket issued by the REST relay-ticket API.</param>
    internal static string BuildConnectionUrl(string hubUrl, string relayTicket)
    {
        var uriBuilder = new UriBuilder(hubUrl);
        var queryToAppend = $"ticket={Uri.EscapeDataString(relayTicket)}";

        if (string.IsNullOrEmpty(uriBuilder.Query) || uriBuilder.Query == "?")
        {
            uriBuilder.Query = queryToAppend;
        }
        else
        {
            var existingQueryParameters = uriBuilder.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(pair => !pair
                    .Split('=', 2)[0]
                    .Equals("ticket", StringComparison.OrdinalIgnoreCase));

            uriBuilder.Query = string.Join('&', existingQueryParameters.Append(queryToAppend));
        }

        return uriBuilder.Uri.AbsoluteUri;
    }

    /// <summary>
    /// Starts the connection to the SignalR relay hub. The start runs outside the lifecycle
    /// loop (so it can never block recovery processing) and its outcome is applied by the loop.
    /// A second start while one is in progress throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(RelayClientPublisher));
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _commands.Writer.WriteAsync(new LifecycleCommand.Start(completion));
        await completion.Task;
    }

    /// <summary>
    /// Publishes a transport message to the relay hub. Every message takes the same unified
    /// pipeline: while the connection is connected the message is sent immediately; while the
    /// connection is recovering (automatic reconnect or ticket-refresh rebuild) it is held in
    /// the bounded pipeline (capacity <c>outboundQueueCapacity</c>) and delivered
    /// in order once connectivity returns. The returned task completes when the message has
    /// been sent, or throws:
    /// <see cref="TransportPublishException"/> with <see cref="PublishFailureReason.QueueFull"/>
    /// when the pipeline is full, or with <see cref="PublishFailureReason.NotConnected"/> when
    /// the publisher is disconnected with no recovery path configured or after a terminal close.
    /// </summary>
    /// <param name="message">The transport message to publish.</param>
    public async Task PublishMessage(TransportMessage message)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(RelayClientPublisher));
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_outbound.Writer.TryWrite(new PendingSend(message, completion)))
        {
            _logger.LogWarning(
                "Message rejected: outbound pipeline is full ({Capacity} messages)",
                _outboundCapacity);
            throw new TransportPublishException(
                PublishFailureReason.QueueFull,
                $"Outbound pipeline is full ({_outboundCapacity} messages).");
        }

        await completion.Task;
    }

    /// <summary>
    /// Subscribes to receive transport messages from the relay.
    /// </summary>
    /// <param name="onMessageReceived">Action called when a transport message is received.</param>
    public void Subscribe(Action<TransportMessage> onMessageReceived)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(RelayClientPublisher));
        }

        ArgumentNullException.ThrowIfNull(onMessageReceived);

        lock (_subscribers)
        {
            _subscribers.Add(onMessageReceived);
        }
    }

    /// <summary>
    /// Asynchronously disposes the publisher and closes the hub connection. Cancels any
    /// in-flight start or rebuild, awaits both internal loops and faults all still-pending
    /// publishes and starts so no caller is left waiting.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await _lifetimeCts.CancelAsync();
        _commands.Writer.TryComplete();
        _outbound.Writer.TryComplete();
        _resumeSignal.Writer.TryComplete();

        foreach (var loopTask in new[] { _lifecycleTask, _pumpTask })
        {
            try
            {
                await loopTask;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Internal loop ended with an error during disposal");
            }
        }

        // A start or rebuild may still be running outside the loops; wait for them so a rebuild's
        // replacement connection is fully settled before we capture, stop and dispose the snapshot.
        foreach (var backgroundTask in new[] { _activeStartTask, _activeRebuildTask })
        {
            if (backgroundTask is null)
            {
                continue;
            }

            try
            {
                await backgroundTask;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Background lifecycle operation ended with an error during disposal");
            }
        }

        // Belt and braces: fault anything still buffered in either channel. The loops normally
        // drain these themselves; ReadAllAsync/WaitToReadAsync under a canceled token do not.
        while (_commands.Reader.TryRead(out var command))
        {
            switch (command)
            {
                case LifecycleCommand.Start start:
                    start.Completion.TrySetException(new ObjectDisposedException(nameof(RelayClientPublisher)));
                    break;
                case LifecycleCommand.RebuildCompleted { NewConnection: { } unprocessed }:
                    // The rebuild result never reached the actor; stop and dispose its
                    // replacement connection so nothing is left live against the relay peer.
                    DetachHandlers(unprocessed);
                    try
                    {
                        if (unprocessed.State != HubConnectionState.Disconnected)
                        {
                            await unprocessed.StopAsync();
                        }

                        await unprocessed.DisposeAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error disposing an unprocessed rebuilt connection during disposal");
                    }

                    break;
            }
        }

        while (_outbound.Reader.TryRead(out var pending))
        {
            pending.Completion.TrySetException(new ObjectDisposedException(nameof(RelayClientPublisher)));
        }

        var connection = _snapshot.Connection;
        DetachHandlers(connection);

        if (connection.State != HubConnectionState.Disconnected)
        {
            await connection.StopAsync();
        }

        await connection.DisposeAsync();
        _lifetimeCts.Dispose();

        RaiseTerminalClosed(null);

        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------------
    // Lifecycle actor
    // ------------------------------------------------------------------

    /// <summary>
    /// Single-consumer lifecycle loop: the sole owner of connection identity, recovery and the
    /// published snapshot. SignalR callbacks and StartAsync post commands here; no handler of
    /// this switch performs an inline long-running operation (starts and rebuilds run as
    /// tracked background tasks posting their results back as commands), so the mailbox stays
    /// responsive and no command can starve another.
    /// </summary>
    private async Task RunLifecycleLoopAsync()
    {
        var current = _snapshot.Connection;
        Task? activeStart = null;
        Task? activeRebuild = null;
        var lastTransitionSequence = 0L;

        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(_lifetimeCts.Token))
            {
                try
                {
                    switch (command)
                    {
                        case LifecycleCommand.Start cmd:
                            if (activeStart is not null)
                            {
                                cmd.Completion.TrySetException(new InvalidOperationException(
                                    "A start operation is already in progress."));
                                break;
                            }

                            if (_snapshot.TerminallyClosed)
                            {
                                cmd.Completion.TrySetException(new TransportPublishException(
                                    PublishFailureReason.NotConnected,
                                    "Relay client was closed terminally; recreate the publisher with a fresh ticket."));
                                break;
                            }

                            if (current.State != HubConnectionState.Disconnected)
                            {
                                cmd.Completion.TrySetResult(null);
                                break;
                            }

                            activeStart = RunStartAsync(current, cmd.Completion);
                            _activeStartTask = activeStart;
                            break;

                        case LifecycleCommand.StartCompleted cmd:
                            activeStart = null;
                            if (cmd.Error is not null)
                            {
                                PublishSnapshot(current, sendEnabled: false);
                                cmd.Completion.TrySetException(cmd.Error);
                            }
                            else
                            {
                                PublishSnapshot(current, sendEnabled: true);
                                cmd.Completion.TrySetResult(null);
                            }

                            break;

                        case LifecycleCommand.HubReconnecting cmd:
                            if (cmd.Sequence <= lastTransitionSequence)
                            {
                                break;
                            }

                            lastTransitionSequence = cmd.Sequence;
                            // SignalR occasionally delivers this callback late — after the
                            // reconnect already completed. Pausing the pipeline against an
                            // observably-connected hub would stall it forever (no further
                            // Reconnected will come), so only pause when the state agrees.
                            if (current.State != HubConnectionState.Connected)
                            {
                                PublishSnapshot(current, sendEnabled: false);
                            }

                            RaiseEvent(() => Reconnecting?.Invoke(cmd.Error));
                            break;

                        case LifecycleCommand.HubReconnected cmd:
                            if (cmd.Sequence <= lastTransitionSequence)
                            {
                                break;
                            }

                            lastTransitionSequence = cmd.Sequence;
                            PublishSnapshot(current, sendEnabled: true);
                            RaiseEvent(() => Reconnected?.Invoke(cmd.ConnectionId));
                            break;

                        case LifecycleCommand.HubClosed cmd:
                            if (!ReferenceEquals(cmd.Connection, current) || _snapshot.TerminallyClosed)
                            {
                                break; // stale notification or already terminal
                            }

                            if (_ticketRefresh is null)
                            {
                                PublishSnapshot(current, sendEnabled: false, terminallyClosed: true);
                                RaiseTerminalClosed(cmd.Error);
                                break;
                            }

                            if (activeRebuild is not null)
                            {
                                break; // coalesce overlapping close notifications
                            }

                            PublishSnapshot(current, sendEnabled: false);
                            activeRebuild = RunRebuildAsync();
                            _activeRebuildTask = activeRebuild;
                            break;

                        case LifecycleCommand.RebuildCompleted cmd:
                            activeRebuild = null;
                            if (cmd.NewConnection is { } rebuilt)
                            {
                                var superseded = current;
                                var newConnection = rebuilt;
                                DetachHandlers(superseded);
                                current = newConnection;
                                PublishSnapshot(newConnection, sendEnabled: true);
                                RaiseEvent(() => Reconnected?.Invoke(newConnection.ConnectionId));
                                _ = DisposeSupersededConnectionAsync(superseded);
                            }
                            else
                            {
                                PublishSnapshot(current, sendEnabled: false, terminallyClosed: true);
                                RaiseTerminalClosed(cmd.Error);
                            }

                            break;
                    }
                }
                catch (Exception ex)
                {
                    // Never let one bad iteration kill the actor.
                    _logger.LogError(ex, "Unexpected error processing a lifecycle command");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown: disposal cancels the lifetime token.
        }
    }

    private void PublishSnapshot(HubConnection connection, bool sendEnabled, bool terminallyClosed = false)
    {
        _snapshot = new ConnectionSnapshot(connection, sendEnabled, _hasRecoveryPath, terminallyClosed);
        // Signal on every transition, not only enables: a parked pump must also wake for
        // terminal close (to fault its backlog) even though sending stays disabled.
        SignalPump();
    }

    private void SignalPump() => _resumeSignal.Writer.TryWrite(0);

    private async Task RunStartAsync(HubConnection connection, TaskCompletionSource<object?> completion)
    {
        Exception? error = null;
        try
        {
            await connection.StartAsync(_lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
            // Disposal owns faulting this completion via the shutdown drain.
            completion.TrySetException(new ObjectDisposedException(nameof(RelayClientPublisher)));
            return;
        }
        catch (Exception ex)
        {
            error = ex;
        }

        _commands.Writer.TryWrite(new LifecycleCommand.StartCompleted(completion, error));
    }

    /// <summary>
    /// Background rebuild body: pure I/O with no shared-state mutation — obtains a fresh ticket,
    /// builds and starts a replacement connection, then posts the result back to the lifecycle
    /// actor, which alone swaps the published snapshot. Terminal failure (refresh failed/null,
    /// or bounded start attempts exhausted) is reported via <see cref="LifecycleCommand.RebuildCompleted"/>.
    /// </summary>
    private async Task RunRebuildAsync()
    {
        RelayTicketRefresh? refresh = null;
        Exception? error = null;

        try
        {
            refresh = await _ticketRefresh!(_lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
            return; // disposal unwinds everything; nothing owed to anyone
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Relay ticket refresh failed; closing the connection terminally");
            error = ex;
        }

        switch (error)
        {
            case null when refresh is null:
                _logger.LogWarning("Relay ticket refresh returned no ticket; closing the connection terminally");
                break;
            case null:
            {
                var newConnection = BuildConnection(_hubUrl, refresh.RelayTicket, refresh.ExpiresAt);
                AttachHandlers(newConnection);

                Exception? lastStartFailure = null;
                for (var attempt = 1; attempt <= MaxRestartAttempts && !_isDisposed; attempt++)
                {
                    try
                    {
                        await newConnection.StartAsync(_lifetimeCts.Token);
                        lastStartFailure = null;
                        break;
                    }
                    catch (OperationCanceledException) when (_isDisposed)
                    {
                        // The replacement never went live; dispose it so it cannot linger.
                        DetachHandlers(newConnection);
                        _ = DisposeSupersededConnectionAsync(newConnection);
                        return;
                    }
                    catch (Exception ex)
                    {
                        lastStartFailure = ex;
                        _logger.LogWarning(
                            ex,
                            "Failed to restart relay connection with fresh ticket (attempt {Attempt}/{MaxAttempts})",
                            attempt,
                            MaxRestartAttempts);
                        try
                        {
                            await Task.Delay(RestartRetryDelay * attempt,
                                _lifetimeCts.Token);
                        }
                        catch (OperationCanceledException) when (_isDisposed)
                        {
                            // The replacement never went live; dispose it so it cannot linger.
                            DetachHandlers(newConnection);
                            _ = DisposeSupersededConnectionAsync(newConnection);
                            return;
                        }
                    }
                }

                if (!_isDisposed && lastStartFailure is null)
                {
                    _commands.Writer.TryWrite(new LifecycleCommand.RebuildCompleted(newConnection, null));
                    return;
                }

                if (lastStartFailure is not null)
                {
                    _logger.LogError(
                        "Giving up restarting relay connection after {MaxAttempts} attempts",
                        MaxRestartAttempts);
                    error = lastStartFailure;
                }

                // The replacement connection never went live; dispose it quietly.
                DetachHandlers(newConnection);
                _ = DisposeSupersededConnectionAsync(newConnection);
                break;
            }
        }

        if (_isDisposed)
        {
            return;
        }

        _commands.Writer.TryWrite(new LifecycleCommand.RebuildCompleted(null, error));
    }

    private async Task DisposeSupersededConnectionAsync(HubConnection connection)
    {
        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error disposing superseded relay connection");
        }
    }

    // ------------------------------------------------------------------
    // Outbound pump
    // ------------------------------------------------------------------

    /// <summary>
    /// Single-consumer outbound pump: the only code that ever sends envelopes. Holds at most one
    /// locally ("held slot") so a failed or paused send keeps its place ahead of everything
    /// still in the channel — FIFO is preserved by construction. Parks when sending is disabled,
    /// wakes on enable / terminal close / disposal, and faults the backlog with NotConnected on
    /// terminal close or when no recovery path exists.
    /// </summary>
    private async Task RunOutboundPumpAsync()
    {
        PendingSend? held = null;
        var heldSendFailures = 0;
        try
        {
            while (true)
            {
                var snapshot = _snapshot;

                if (snapshot.TerminallyClosed)
                {
                    FaultNotConnected(held);
                    held = null;
                    while (_outbound.Reader.TryRead(out var pending))
                    {
                        FaultNotConnected(pending);
                    }

                    // Keep rejecting anything that arrives later; exit when the channel drains
                    // during disposal.
                    if (!await _outbound.Reader.WaitToReadAsync(_lifetimeCts.Token))
                    {
                        return;
                    }

                    continue;
                }

                if (!snapshot.SendEnabled || snapshot.Connection.State != HubConnectionState.Connected)
                {
                    if (!snapshot.HasRecoveryPath)
                    {
                        // No reconnect window and no refresh delegate: a disconnect is permanent.
                        // Preserve the legacy immediate-rejection contract.
                        FaultNotConnected(held);
                        held = null;
                        while (_outbound.Reader.TryRead(out var pending))
                        {
                            FaultNotConnected(pending);
                        }

                        if (!await _outbound.Reader.WaitToReadAsync(_lifetimeCts.Token))
                        {
                            return; // disposal completed the outbound channel
                        }

                        continue;
                    }

                    if (!await _resumeSignal.Reader.WaitToReadAsync(_lifetimeCts.Token))
                    {
                        return; // disposal completed the signal channel
                    }

                    _resumeSignal.Reader.TryRead(out _);
                    continue;
                }

                if (held is null)
                {
                    if (!_outbound.Reader.TryRead(out var next))
                    {
                        if (!await _outbound.Reader.WaitToReadAsync(_lifetimeCts.Token))
                        {
                            return; // disposal completed the outbound channel
                        }

                        continue;
                    }

                    held = next;
                    heldSendFailures = 0;
                }

                var currentHeld = held;
                try
                {
                    await SendEnvelopeAsync(snapshot.Connection, currentHeld.Message);
                    currentHeld.Completion.TrySetResult(null);
                    held = null;
                    heldSendFailures = 0;
                }
                catch (Exception ex) when (_isDisposed && ex is not OperationCanceledException)
                {
                    // Disposal interrupted a send with a non-cancellation failure
                    // (e.g. a close-frame HubException): fault the held message and exit.
                    _logger.LogDebug(ex, "Outbound send failed during disposal");
                    FaultDisposed(held);
                    while (_outbound.Reader.TryRead(out var pending))
                    {
                        FaultDisposed(pending);
                    }

                    return;
                }
                catch (Exception ex) when (!_isDisposed)
                {
                    heldSendFailures++;
                    if (heldSendFailures >= MaxSendRetryFailures)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to send queued message after {MaxFailures} consecutive attempts; faulting it",
                            MaxSendRetryFailures);
                        currentHeld.Completion.TrySetException(new TransportPublishException(
                            PublishFailureReason.NotConnected,
                            "Failed to send message after repeated attempts while disconnected."));
                        held = null;
                        heldSendFailures = 0;
                        continue;
                    }

                    _logger.LogError(ex, "Failed to send queued message; retrying");
                    await Task.Delay(SendRetryDelay, _lifetimeCts.Token);
                    // Loop back: the snapshot is re-read, so a connection drop parks the pump
                    // with the held message intact, ahead of everything behind it.
                }
            }
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
            FaultDisposed(held);
            while (_outbound.Reader.TryRead(out var pending))
            {
                FaultDisposed(pending);
            }
        }
    }

    private void FaultNotConnected(PendingSend? pending)
    {
        if (pending is null)
        {
            return;
        }

        _logger.LogWarning("Message rejected: relay client is not connected");
        pending.Completion.TrySetException(new TransportPublishException(
            PublishFailureReason.NotConnected,
            "Relay client is not connected."));
    }

    private void FaultDisposed(PendingSend? pending) =>
        pending?.Completion.TrySetException(new ObjectDisposedException(nameof(RelayClientPublisher)));

    private async Task SendEnvelopeAsync(HubConnection connection, TransportMessage message)
    {
        var serializedPayload = JsonSerializer.Serialize(message);
        var envelope = new RelayEnvelope(
            SenderId: connection.ConnectionId ?? string.Empty,
            Payload: serializedPayload,
            SchemaVersion: "1.0.0",
            SequenceNumber: ++_sequenceNumber,
            Timestamp: DateTime.UtcNow);

        await connection.InvokeAsync("Relay", _roomCode, envelope);
    }

    // ------------------------------------------------------------------
    // Connection wiring (called from the constructor and the lifecycle actor only)
    // ------------------------------------------------------------------

    private HubConnection BuildConnection(string hubUrl, string relayTicket, DateTimeOffset? expiresAt)
    {
        var builder = new HubConnectionBuilder()
            .WithUrl(BuildConnectionUrl(hubUrl, relayTicket), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
            });

        if (expiresAt is { } ticketExpiry)
        {
            builder = builder.WithAutomaticReconnect(new RelayTicketExpiryRetryPolicy(ticketExpiry));
        }

        return builder.Build();
    }

    private void AttachHandlers(HubConnection connection)
    {
        // Return a completed Task so the SignalR client awaits the handler before
        // dispatching the next envelope, preserving delivery order to subscribers.
        connection.On<RelayEnvelope>("OnReceive", envelope =>
        {
            HandleEnvelopeReceived(envelope);
            return Task.CompletedTask;
        });
        connection.On<HubError>("OnError", HandleHubError);
        connection.On<string>("OnPeerConnected", HandlePeerConnected);
        connection.On<string>("OnPeerDisconnected", HandlePeerDisconnected);

        connection.Reconnecting += OnReconnecting;
        connection.Reconnected += OnReconnected;

        // The Closed handler must know which connection closed so notifications from
        // superseded connections can be identified; the delegate is kept to allow detaching.
        var closedHandler = new Func<Exception?, Task>(exception => OnClosed(connection, exception));
        lock (_closedHandlersGate)
        {
            _closedHandlers[connection] = closedHandler;
        }

        connection.Closed += closedHandler;
    }

    private void DetachHandlers(HubConnection connection)
    {
        connection.Reconnecting -= OnReconnecting;
        connection.Reconnected -= OnReconnected;

        lock (_closedHandlersGate)
        {
            if (!_closedHandlers.Remove(connection, out var closedHandler))
            {
                return;
            }

            connection.Closed -= closedHandler;
        }
    }

    private Task OnReconnecting(Exception? exception)
    {
        _logger.LogInformation(exception, "Relay client reconnecting");
        _commands.Writer.TryWrite(new LifecycleCommand.HubReconnecting(
            Interlocked.Increment(ref _transitionSequence), exception));
        return Task.CompletedTask;
    }

    private Task OnReconnected(string? connectionId)
    {
        _logger.LogInformation("Relay client reconnected with connection ID {ConnectionId}", connectionId);
        _commands.Writer.TryWrite(new LifecycleCommand.HubReconnected(
            Interlocked.Increment(ref _transitionSequence), connectionId));
        return Task.CompletedTask;
    }

    private Task OnClosed(HubConnection connection, Exception? exception)
    {
        if (_isDisposed)
        {
            return Task.CompletedTask;
        }

        if (exception is not null)
        {
            _logger.LogError(exception, "Relay client connection closed with error");
        }
        else
        {
            _logger.LogInformation("Relay client connection closed");
        }_commands.Writer.TryWrite(new LifecycleCommand.HubClosed(connection, exception));
        return Task.CompletedTask;
    }

    private void RaiseTerminalClosed(Exception? exception)
    {
        // Raise Closed at most once: disposal and a concurrent terminal rebuild
        // failure must not produce duplicate notifications.
        if (Interlocked.Exchange(ref _terminalClosedRaised, 1) != 0)
        {
            return;
        }

        RaiseEvent(() => Closed?.Invoke(exception));
    }

    // ------------------------------------------------------------------
    // Inbound dispatch (untouched subscriber trampoline)
    // ------------------------------------------------------------------

    private void HandleEnvelopeReceived(RelayEnvelope envelope)
    {
        if (_isDisposed || string.IsNullOrEmpty(envelope.Payload))
        {
            return;
        }

        try
        {
            var message = JsonSerializer.Deserialize<TransportMessage>(envelope.Payload);
            if (message is null)
            {
                return;
            }

            NotifySubscribers(message);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Malformed payload received in envelope from {SenderId}", envelope.SenderId);
        }
    }

    private void HandleHubError(HubError error)
    {
        if (_isDisposed)
        {
            return;
        }

        if (error.Code == HubErrorCode.HostDisconnected)
        {
            _logger.LogWarning("Host disconnected: {Message}", error.Message);
            RaiseEvent(HostDisconnected);
            return;
        }

        RaiseEvent(() => HubErrorReceived?.Invoke(error));
    }

    private void HandlePeerConnected(string peerId)
    {
        if (_isDisposed)
        {
            return;
        }

        RaiseEvent(() => PeerConnected?.Invoke(peerId));
    }

    private void HandlePeerDisconnected(string peerId)
    {
        if (_isDisposed)
        {
            return;
        }

        RaiseEvent(() => PeerDisconnected?.Invoke(peerId));
    }

    private void RaiseEvent(Action? handler)
    {
        if (handler is null) return;
        if (_syncContext is not null)
            _syncContext.Post(_ => handler(), null);
        else
            handler();
    }

    private void NotifySubscribers(TransportMessage message)
    {
        Action<TransportMessage>[] currentSubscribers;
        lock (_subscribers)
        {
            currentSubscribers = [.. _subscribers];
        }

        if (_syncContext is not null)
        {
            lock (_dispatchLock)
            {
                _pendingNotifications.Enqueue((message, currentSubscribers));
                if (_dispatchPending)
                {
                    return;
                }

                _dispatchPending = true;
            }

            _syncContext.Post(_ => DispatchPendingNotifications(), null);
            return;
        }

        foreach (var subscriber in currentSubscribers)
        {
            try
            {
                subscriber(message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error notifying subscriber");
            }
        }
    }

    private void DispatchPendingNotifications()
    {
        while (true)
        {
            (TransportMessage message, Action<TransportMessage>[] subscribers) item;
            lock (_dispatchLock)
            {
                if (_pendingNotifications.Count == 0)
                {
                    _dispatchPending = false;
                    return;
                }

                item = _pendingNotifications.Dequeue();
            }

            foreach (var subscriber in item.subscribers)
            {
                try
                {
                    subscriber(item.message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error notifying subscriber via sync context");
                }
            }
        }
    }
}
