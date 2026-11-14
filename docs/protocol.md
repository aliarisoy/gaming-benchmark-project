# RigSight Protocol v1

Two transports share the same JSON message vocabulary:

- **WebSocket** for LAN data: `ws://<pc-ip>:47823/v1/stream` (default port; configurable).
- **BLE GATT** for pairing, prompts, control, and fallback metrics.

All messages are UTF-8 JSON objects with a `type` field. Unknown fields are ignored.
The protocol version is `1`.

## WebSocket

### Handshake

The client connects, then sends within 10 seconds:

```json
{
  "type": "hello",
  "protocolVersion": 1,
  "token": "<base64url token>",
  "deviceName": "Test Phone",
  "appVersion": "0.1.0"
}
```

On success the server replies:

```json
{
  "type": "hello_ack",
  "protocolVersion": 1,
  "serverVersion": "0.1.0",
  "pcName": "DESKTOP-RIG",
  "pcId": "…",
  "token": "<issued only when newly paired>",
  "session": null,
  "games": [{ "id": "…", "name": "…", "exeName": "…" }]
}
```

If a session is already running, `session` is populated. The server then follows up
with a separate `prompt`, `session_started`, and/or `sample` snapshot message. There
is no `lastSample` field on `hello_ack`.

If the token is invalid the server sends:

```json
{ "type": "error", "code": "unauthorized", "message": "Unknown device. Pair with the code shown on the PC." }
```

and closes with WebSocket close code `1000` (older builds used `4401`). The client
should re-pair rather than retry forever. A first-time `hello` may carry `code`
instead of `token`; on success the ack includes the newly issued token.

Other handshake errors use `code`: `bad_hello`, `protocol_mismatch`, `pair_failed`.

### Server → client messages

| type | payload |
| --- | --- |
| `prompt` | `prompt: {id, gameId, gameName, exeName, expiresAt}` |
| `prompt_closed` | `promptId, reason: "started" \| "declined" \| "expired"` |
| `session_started` | `session: {id, gameId, gameName, exeName, startedAt, sampleIntervalMs}` |
| `sample` | `sample: TelemetrySample` |
| `session_ended` | `summary: SessionSummary` |
| `sessions` | `sessions: SessionSummary[]` (history list) |
| `session_detail` | `summary: SessionSummary, samples: TelemetrySample[]` |
| `pong` | heartbeat reply |
| `error` | `code, message` |

### Client → server messages

| type | payload |
| --- | --- |
| `start_session` | `promptId` (or `gameId` for a direct start) |
| `decline_prompt` | `promptId` |
| `stop_session` | `sessionId` |
| `list_sessions` | — |
| `get_session` | `sessionId` |
| `ping` | — |

The server sends `pong` for heartbeat. Samples are broadcast to all authenticated
clients of the active session. `get_session` returns at most 1500 evenly spaced
samples, which keeps phone charts and history sync small while the PC keeps the raw
JSONL recording.

### TelemetrySample

```json
{
  "t": 12345,
  "fps": 142.6,
  "frameTimeMs": 7.01,
  "low1Fps": 118.2,
  "low01Fps": 104.7,
  "cpuLoadPct": 41.2,
  "cpuTempC": 67.0,
  "cpuPowerW": 96.4,
  "cpuClockMhz": 5088,
  "gpuLoadPct": 98.0,
  "gpuTempC": 71.0,
  "gpuHotspotC": 83.0,
  "gpuPowerW": 312.5,
  "gpuClockMhz": 1920,
  "vramUsedMb": 7421,
  "vramTotalMb": 10240,
  "ramUsedMb": 18320,
  "ramTotalMb": 32768
}
```

`t` is milliseconds since session start. Any unavailable metric is `null` (omitted
or JSON `null`). Values are already in the units indicated by the field name.

### SessionSummary

```json
{
  "id": "01J…",
  "gameId": "…",
  "gameName": "…",
  "exeName": "game.exe",
  "startedAt": "2026-11-13T20:14:03Z",
  "endedAt": "2026-11-13T21:02:11Z",
  "durationMs": 2888000,
  "sampleCount": 5776,
  "avgFps": 141.2,
  "medianFps": 144.0,
  "low1Fps": 109.4,
  "low01Fps": 92.1,
  "avgFrameTimeMs": 7.08,
  "maxCpuTempC": 79.0,
  "maxGpuTempC": 74.0,
  "maxCpuPowerW": 118.2,
  "maxGpuPowerW": 342.0,
  "avgCpuLoadPct": 44.1,
  "avgGpuLoadPct": 97.2,
  "maxVramUsedMb": 8112,
  "stopReason": "game_exit",
  "schemaVersion": 1
}
```

`stopReason`: `user`, `phone`, `game_exit`, `agent_shutdown`, `interrupted`.

## BLE GATT

### UUIDs

| Object | UUID |
| --- | --- |
| Service | `8f1e0001-5c3a-4f8e-9b7d-2a6c1f4e9a10` |
| Control (write) | `8f1e0002-5c3a-4f8e-9b7d-2a6c1f4e9a10` |
| Event (read/notify) | `8f1e0003-5c3a-4f8e-9b7d-2a6c1f4e9a10` |

The event characteristic's read value is:

```json
{ "type": "device_info", "pcName": "DESKTOP-RIG", "protocolVersion": 1, "requiresPairing": true }
```

### Framing

BLE writes and notifications may be split into fragments:

```
byte 0     0x52 ('R')
byte 1     frame version = 0x01
byte 2-3   message id, uint16 big-endian
byte 4     fragment index (0-based)
byte 5     fragment count
byte 6..   UTF-8 JSON payload fragment
```

A full message is sent as one fragment when it fits the negotiated MTU. The receiver
reassembles by `(message id, fragment index, fragment count)` and ignores malformed
or incomplete messages after 5 seconds.

### Messages

Client → server:

```json
{ "type": "ble_hello", "deviceName": "Test Phone", "code": "123456" }
{ "type": "ble_hello", "deviceName": "Test Phone", "token": "<saved token>" }
{ "type": "start_session", "promptId": "…" }
{ "type": "decline_prompt", "promptId": "…" }
{ "type": "stop_session", "sessionId": "…" }
{ "type": "ping" }
```

Server → client:

```json
{
  "type": "ble_hello_ack",
  "ok": true,
  "pcName": "DESKTOP-RIG",
  "token": "<issued once>",
  "wsPort": 47823,
  "wsHosts": ["192.168.1.50"],
  "session": null
}
```

`ble_hello_ack` with `ok:false` includes `reason` (`bad_code`, `locked_out`,
`expired`). After pairing, the server sends the same `prompt`, `session_started`,
`session_ended`, and low-rate `sample` messages as WebSocket. BLE fallback samples
are sent at 1 Hz and may omit the chart-only fields.

## Pairing code rules

- Six digits, generated on agent start and after **Regenerate**.
- Valid for 10 minutes, maximum 3 failed attempts, then the code is rotated.
- Stored only in memory on the PC; the phone's issued token is stored on both sides.
- Revoking a device deletes its token on the PC; the phone must re-pair.

## Versioning

Additive fields are backward-compatible within v1. Breaking changes bump
`protocolVersion`; the PC and phone refuse mismatched major versions with a clear
message. Session records carry `schemaVersion` for offline migration.
