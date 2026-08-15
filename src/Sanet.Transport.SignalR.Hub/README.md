# Sanet.Transport.SignalR.Hub

Cloud relay room-management service. Hosts a SignalR hub for real-time message relay and REST endpoints for room lifecycle management.

## Prerequisites

- .NET 10 SDK
- Docker (optional, for containerized run)

## Configuration

Configuration is managed via `appsettings.json` under the `Hub` section:

```json
{
  "Hub": {
    "ApiKey": "",
    "MaxConcurrentRooms": 100,
    "RoomTtlSeconds": 7200
  }
}
```

- **ApiKey**: Shared key required by REST callers (sent via `X-Api-Key` header). Must be set to a non-empty value — all `/api/*` requests are rejected with 401 if empty.
- **MaxConcurrentRooms**: Maximum number of active rooms at once.
- **RoomTtlSeconds**: Time-to-live for inactive rooms before garbage collection.

Full option list in `Configuration/HubOptions.cs`.

## Running Locally

### With .NET SDK

Set environment to Development:
```bash
$env:ASPNETCORE_ENVIRONMENT="Development"   # PowerShell
export ASPNETCORE_ENVIRONMENT="Development" # bash
```

```bash
dotnet run --project src/Sanet.Transport.SignalR.Hub/Sanet.Transport.SignalR.Hub.csproj
```

The service starts on `http://localhost:5000` (ASP.NET default) with the `Development` environment profile.

Set the API key via environment variable or `appsettings.Development.json`:

```bash
$env:Hub__ApiKey="dev-key"   # PowerShell
export Hub__ApiKey="dev-key"  # bash
```

Or add `"ApiKey": "dev-key"` to the `Hub` section in `appsettings.Development.json`.

### With Docker

Build from the repository root:

```bash
docker build -f src/Sanet.Transport.SignalR.Hub/Dockerfile -t sanet-transport-hub .
docker run -p 8080:8080 -e Hub__ApiKey="dev-key" sanet-transport-hub
```

The container listens on `http://localhost:8080` in `Production` mode.

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/health` | Health check (returns status, service name, version) |
| POST | `/api/rooms` | Create a room (requires `X-Api-Key` header) |
| POST | `/api/rooms/{roomCode}/join` | Join a room by code (requires `X-Api-Key` header, rate-limited per IP) |
| POST | `/api/rooms/{roomCode}/ready` | Mark a room ready to accept joiners (requires `X-Api-Key` + `Session-Token` header, host only) |
| POST | `/api/rooms/{roomCode}/close` | Close a room (requires `X-Api-Key` + `Session-Token` header, host only) |
| DELETE | `/api/rooms/{roomCode}/members/{playerId}` | Remove a member (requires `X-Api-Key` + `Session-Token` header, host only) |
| WebSocket | `/hubs/relay` | SignalR hub for message relay (requires `sessionToken` query parameter) |

## Connecting Clients

Once the Hub is running and the API key is set, any client built on `Sanet.Transport.SignalR.Client` can join a room. Connect the relay publisher to the hub URL, e.g.:

```bash
$env:RELAY_BASE_URL = "http://localhost:5000"
```
