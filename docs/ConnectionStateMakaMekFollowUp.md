# Connection State in MakaMek — Follow-up Guide

This document describes how to consume the new `TransportConnectionState` API in the
MakaMek client once the `Sanet.Transport` package (>= 1.7.0) is upgraded by the transport
team. It is a follow-up task tracked separately; no reconnect or ticket-refresh logic is
implemented here.

## What the transport now exposes

Every `ITransportPublisher` (LAN `SignalRClientPublisher` and relay `RelayClientPublisher`)
has:

- `TransportConnectionState ConnectionState` — the current connectivity state.
- `event Action<TransportConnectionState>? ConnectionStateChanged` — raised on every transition.

`TransportConnectionState` values:

| Value | Meaning |
| --- | --- |
| `Connecting` | connection attempt in progress |
| `Connected` | established and operational |
| `Reconnecting` | lost, auto-reconnect / recovery in progress (non-terminal) |
| `Disconnected` | not active (non-terminal) |
| `Closed` | **terminal** — a new publisher must be created |

Important distinction: the event reports the **transport** connection only. It does **not**
reflect room membership — `PeerConnected` / `PeerDisconnected` / `HostDisconnected` still
drive the roster, not `ConnectionState`.

## Recommended wiring (GameManager)

1. After creating the publisher in the game session, subscribe once:

```csharp
publisher.ConnectionStateChanged += state =>
{
    // Publish onto your reactive stream / ViewModel messenger.
    _connectionStateSubject.OnNext(state);
};
```

2. Expose `ConnectionState` as an `IObservable<TransportConnectionState>` (or a
   `BehaviorSubject` seeded with `publisher.ConnectionState`) so ViewModels can compose it.

3. Do **not** attempt reconnect or ticket-refresh in MakaMek. Recovery is the transport's
   job (auto-reconnect within the ticket window, or the `TicketRefresh` delegate). On
   `Closed` the publisher is dead: tear down the session state, and have the user re-join
   (new room membership → new publisher).

## UI guidance

Show a status banner / badge and disable gameplay input while:

- `Connecting` — "Connecting…"
- `Reconnecting` — "Connection lost — reconnecting…" (do not leave the game or clear data)
- `Disconnected` — idle / rebuilding variant of the same banner

Distinguish `Closed` (red, terminal — "Connection closed", re-join required) from the
non-terminal states. On `Connected`, hide the banner and re-enable input.

A suggested mapping for a reactive app:

```csharp
var connectionStatus = _connectionStateSubject
    .Select(state => state switch
    {
        TransportConnectionState.Connected => ConnectionUiStatus.Connected,
        TransportConnectionState.Connecting => ConnectionUiStatus.Connecting,
        TransportConnectionState.Reconnecting => ConnectionUiStatus.Reconnecting,
        TransportConnectionState.Disconnected => ConnectionUiStatus.Disconnected,
        TransportConnectionState.Closed => ConnectionUiStatus.TerminalClosed,
        _ => ConnectionUiStatus.Disconnected,
    });
```

Input gating: block send/action commands while status is `Connecting`, `Reconnecting`,
`Disconnected` or `TerminalClosed`. Sends during recovery are held/drained by the transport
pipeline; the UI should still reflect "not yet delivered".

## Notes

- Raise happens on the `SynchronizationContext` captured at publisher construction (relay
  variant) — marshal to the UI thread as usual in MakaMek.
- Ticket #52 (broader recovery / re-join UX) is tracked separately; this wiring is only the
  surface-level state surfacing.