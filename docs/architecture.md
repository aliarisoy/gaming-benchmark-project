# Architecture

## Goals

1. **Never lose a session because the phone is away.** Recording is owned by the PC.
2. **Never block the game display.** RigSight draws no overlay. The phone is the
   dashboard.
3. **Explicit start.** A detected game raises a prompt on the PC and, when connected,
   on the phone. Either device can start; nothing records silently.
4. **Local-only.** No cloud dependency for the core flow.

## Components

### PC agent (`pc/src/RigSight.Agent`)

A single elevated WPF tray application. It owns:

- **Game library** — user-added `.exe` profiles (`name`, `exeName`, optional path).
- **Game watcher** — polls the process list every 1.5 s and matches configured games.
  It remembers process IDs it has already prompted for, so a game is not re-prompted
  until it exits. Sessions end automatically a few seconds after the tracked PID exits.
- **Session service** — a small state machine:
  `Idle → Prompting → Recording → Finalizing → Idle`.
  Start can come from the PC prompt, a global hotkey, or the phone (WS/BLE action).
- **Telemetry sampler** — ticks at the configured rate (default 2 Hz) and combines the
  latest hardware snapshot with PresentMon's rolling frame statistics.
- **Session recorder** — writes raw samples as JSONL and an indexed summary to SQLite.
- **Transport** — a LAN WebSocket server for rich data and a BLE GATT server for
  pairing, prompts, control, and low-rate fallback.
- **Prompt window** — compact, topmost, auto-dismisses after 60 s. The phone prompt
  remains valid for the same window.

### Sensor stack

| Metric | Source |
| --- | --- |
| CPU load, package temp, package power, core clocks, fans | LibreHardwareMonitorLib |
| GPU load, temp, power, clocks, VRAM | LibreHardwareMonitorLib (NVML-backed for NVIDIA) |
| RAM used/total | LibreHardwareMonitorLib |
| FPS, frame time, 1% / 0.1% lows | Intel PresentMon console app (ETW) |

PresentMon is launched **per session** with the tracked PID:
`--process_id <pid> --output_stdout --no_console_stats --terminate_on_proc_exit`.
CSV rows are parsed as they arrive. Rolling FPS uses a 1 s frame-count window; the
1% low uses the worst 1% of frame times over the last 60 s. Raw samples are stored at
the sampler rate, not per frame.

**Why not an overlay?** Overlays inject into the game, can trip anti-cheat, and cost
frames. PresentMon consumes OS ETW events instead. The only driver usage is
LibreHardwareMonitor's CPU sensor driver; if that is blocked, RigSight degrades to
GPU + FPS telemetry and shows a warning.

### Android app (`android/`)

- **Foreground service** (`connectedDevice`) keeps the BLE link and/or WebSocket open
  while the app is backgrounded or the screen is off. A low-importance ongoing
  notification reflects the connection/session.
- **Connection coordinator** prefers WebSocket for telemetry but always keeps BLE
  for prompts and control. On BLE `hello_ack`, it learns the PC's LAN endpoint and
  upgrades. If Wi-Fi fails, compact samples continue over BLE.
- **Notification flow** uses `POST_NOTIFICATIONS` and a high-importance "Game
  detected" channel with `Start` / `Not now` actions. Actions are delivered to the
  foreground service (no activity trampoline) and forwarded to the PC.
- **Dashboard** is a Compose screen built for portrait first: an FPS hero card, low
  stats, a custom Canvas chart, then thermals, memory, and the active link. It also
  works in landscape; charts are hand-drawn, so there is no heavyweight chart library.
- **History** uses a plain `SQLiteOpenHelper` for synced summaries and downsampled
  samples, with detail charts cached for offline viewing. The PC remains the source
  of truth; history summaries sync on connect and detail samples sync on demand.

## Transport decision

BLE alone can carry the data, but it is fragile for rich charts and history and is
affected by adapter/MTU quirks. Wi-Fi alone needs discovery and fails on locked-down
guest networks. We use **both**:

| Link | Used for | Why |
| --- | --- | --- |
| BLE GATT | discovery, pairing, game prompts, start/stop, connection heartbeat, low-rate fallback | Always available, low power, works without a shared network |
| LAN WebSocket | live 2 Hz samples, history sync, richer control | Simple, fast, reliable on a trusted home network |

The app remains fully usable with **BLE only** (2 Hz → 1 Hz metrics). The PC UI shows
which transport each phone is using.

## Discovery and pairing

1. The agent starts a GATT service and advertises it (connectable + discoverable).
   Windows controls the advertised name (the PC's Bluetooth name).
2. The phone scans for the RigSight service UUID, connects, and reads a `device_info`
   characteristic (PC name, protocol version, pairing requirement).
3. First pairing: the PC displays a six-digit code. The phone writes `ble_hello` with
   the code. On success the PC returns a random bearer token and its LAN endpoints.
4. The phone stores the token and tries the WebSocket endpoints in order. BLE remains
   connected for prompts.
5. Re-pairing is possible by revoking devices on the PC or clearing the token on the
   phone.

## Session lifecycle

```
game.exe starts
      │
      ▼
 PC prompt ──────phone prompt (BLE/WS)──────┐
      │ Start                    Start     │
      ▼                                    ▼
 SessionService.Start ──▶ session_started broadcast
      │                                    │
      ├─ sample tick ──▶ sample broadcast ──▶ phone dashboard
      ├─ PresentMon frames ──▶ rolling stats
      │
 game.exe exits (or Stop)
      ▼
 Finalize: summary, SQLite index, JSONL flush
      │
      ├─ session_ended broadcast + phone notification
      └─ phone may request history/samples
```

If the phone is disconnected at prompt time, only the PC prompt exists. If it
reconnects during an active session, it receives the current session + latest sample
snapshot immediately.

## Storage

PC (`%LOCALAPPDATA%\RigSight\`):

```
config.json                     agent settings, token, paired phones
sessions.db                     session index + summaries (SQLite)
sessions/<sessionId>/meta.json  session metadata
sessions/<sessionId>/samples.jsonl
logs/agent-YYYYMMDD.log
```

Phone: SharedPreferences for the token/settings and a small SQLite database for
synced history (summary + downsampled samples).

## Failure modes

| Failure | Behavior |
| --- | --- |
| Phone disconnected / asleep / app switched | PC keeps recording; on reconnect it gets the active state. |
| Wi-Fi drops | BLE fallback continues control and 1 Hz metrics; WS reconnects. |
| BLE adapter missing/peripheral unsupported | Agent logs a warning; Wi-Fi + manual IP still work. |
| PresentMon blocked (ETW/anti-cheat) | FPS shows `—`; hardware metrics and recording continue. |
| LHM driver blocked / not elevated | Safe mode: GPU (NVML) + CPU load + FPS; banner in UI. |
| Game crashes | Process watcher ends the session after a short grace period. |
| PC agent crashes | Last completed sample is on disk; session is marked `interrupted` on next launch. |

## Security boundaries

- LAN WebSocket is token-authenticated but unencrypted in the MVP. Bind only to LAN;
  never port-forward it.
- BLE pairing code is rate-limited (3 attempts) and expires after 10 minutes.
- Tokens are stored in `%LOCALAPPDATA%` (PC) and private app storage (Android).
- No admin operations are exposed over the network. Remote commands are limited to
  start/stop/decline/history requests.

## Known MVP limitations

- Single PC, single active phone dashboard (multiple WS clients may observe).
- NVIDIA is the validated GPU path. LHM covers AMD/Intel, but power sensors vary.
- Windows may show a firewall prompt the first time the WebSocket binds.
- Exclusive-fullscreen games may hide the PC prompt. Use the global hotkey
  (`Ctrl+Alt+G`) or the phone notification.
- PresentMon writes per-frame CSV at high FPS; parsing is lightweight but not free.
  PresentMon is bundled/downloaded separately, never committed.
