# RigSight

> Your rig's vitals, on your phone. No overlay, no second monitor.

RigSight is a local-first PC + Android telemetry companion for gaming. It detects
games you've added, asks whether to record, and streams live CPU/GPU/thermal/power/FPS
data to your phone. The phone is a true second screen: it can stay in your hand or on
a stand while the game owns the PC display.

**Status:** early MVP. The Windows PC agent is implemented and validated on Windows 11.
The Android app builds (debug APK + lint clean) and is awaiting physical-device
validation.

## What works

- Windows 11 PC agent (WPF) with tray icon, game library, launch detection, prompts,
  hotkeys, session recording, and a live dashboard.
- Hardware telemetry through LibreHardwareMonitor (CPU load/temp/package power/clocks,
  NVIDIA GPU load/temp/power/clocks/VRAM, RAM) and per-frame FPS/frame-time capture
  through Intel PresentMon (ETW, no injection, no overlay).
- Android app (Kotlin/Compose) with BLE discovery and pairing, notification
  prompts, a responsive portrait or landscape live dashboard, cached history, and
  session detail charts. The debug APK builds and passes lint; physical-device
  validation is the next milestone.
- Hybrid link: BLE stays connected for prompts and control; the app automatically
  upgrades to a local WebSocket for high-rate metrics when both devices are on the
  same Wi-Fi. BLE-only is supported as a fallback.
- Sessions are recorded on the PC (JSONL + SQLite index), with summaries synced to
  the phone and downsampled chart samples cached for offline viewing. CSV export and
  the raw JSONL files live on the PC.
- If the phone sleeps, switches apps, or disconnects, PC recording continues. The
  Android foreground service keeps the connection alive in the background.

## Architecture at a glance

```
┌──────────────────────────── Windows 11 PC ────────────────────────────┐
│  RigSight.Agent (WPF, elevated)                                       │
│  ┌────────────┐  ┌───────────────────┐  ┌──────────────────────────┐  │
│  │ GameWatcher│─▶│ SessionService    │─▶│ SessionRecorder (JSONL)  │  │
│  └────────────┘  │  prompt → record  │  │ SQLite session index     │  │
│                  └────────┬──────────┘  └──────────────────────────┘  │
│  ┌────────────────────────▼──────────────────────────────────────┐    │
│  │ Telemetry: LibreHardwareMonitor + PresentMon                  │    │
│  └────────────────────────┬──────────────────────────────────────┘    │
│  ┌────────────────────────▼──────────┐   ┌────────────────────────┐   │
│  │ WebSocket server (LAN, token auth)│   │ BLE GATT server        │   │
│  │ ws://<pc>:47823/v1/stream          │   │ prompts/control/fallback│  │
│  └────────────────────────┬──────────┘   └───────────┬────────────┘   │
└───────────────────────────┼──────────────────────────┼────────────────┘
                            │ Wi-Fi (primary data)     │ BLE (always-on control)
                     ┌──────▼──────────────────────────▼──────┐
                     │ Android app (Kotlin/Compose)            │
                     │ foreground service · notification       │
                     │ live dashboard · local history          │
                     └─────────────────────────────────────────┘
```

See [docs/architecture.md](docs/architecture.md), [docs/protocol.md](docs/protocol.md),
and [docs/ux.md](docs/ux.md) for the details.

## Repository layout

```
pc/                     .NET 10 solution
  src/RigSight.Core     models, protocol, persistence, config
  src/RigSight.Windows  sensors, BLE, WebSocket, game watching
  src/RigSight.Agent    WPF tray app and UI
android/                Kotlin + Jetpack Compose app
docs/                   architecture, protocol, UX, setup
scripts/                toolchain + development helpers
tools/presentmon/       PresentMon binary (downloaded, not committed)
```

## Quick start (development)

```powershell
# 1. Install the user-local toolchain (no admin required for the SDKs)
.\scripts\setup-dev.ps1

# 2. Build and run the PC agent (UAC prompt: elevation is required for CPU sensors)
.\scripts\run-agent.ps1

# 3. Build the Android APK
.\scripts\build-android.ps1

# Or build and install it on a USB-connected phone
.\scripts\build-android.ps1 -Install
```

The agent needs administrator rights for CPU package temperature/power through
LibreHardwareMonitor. Without elevation it still runs in **safe mode** with GPU-only
telemetry. See [docs/setup.md](docs/setup.md).

## Demo mode

No game running? Use **Start demo session** from the tray menu (or launch
`RigSight.Agent.exe --demo`). This produces realistic synthetic telemetry so the
Android dashboard, charts, and history can be tested end-to-end.

To test only the phone app without the PC agent, run the dependency-free mock PC:

```powershell
node scripts/mock-pc.mjs --prompt
```

Connect to the printed LAN address with pairing code `123456`. See
[docs/setup.md](docs/setup.md#mock-pc) for the other mock modes.

## Security and privacy

- Everything stays on your local network. No cloud, no account, no telemetry.
- First-time pairing uses a six-digit code shown on the PC. The phone receives a
  random bearer token; subsequent WebSocket sessions require it.
- The WebSocket server binds to the LAN and serves unencrypted `ws://` in this MVP.
  The default port is 47823; it is configurable in the agent. Do not expose the
  stream port to the internet; use a trusted network.
- BLE first pairing is not bonded/encrypted in this MVP. Code + token protect the
  session, but a nearby attacker could observe traffic during pairing. Bonding/TLS
  is on the roadmap.
- Full sensor mode loads a kernel driver for CPU telemetry. Some anti-cheat
  software may object. PresentMon uses ETW (no injection) and no overlay is drawn
  over the game. Use safe mode for strict anti-cheat titles.

## Roadmap

- [x] PC telemetry core, PresentMon frame capture, session recorder
- [x] Game library + detection + PC/phone prompts
- [x] BLE GATT server, WebSocket server, pairing
- [x] Android pairing, foreground service, live dashboard, history
- [x] Session detail + charts on phone (summaries synced, downsampled samples)
- [ ] Physical Android device validation
- [ ] BLE bonding + encrypted transport, QR pairing
- [ ] Wi-Fi discovery (mDNS), TLS pinning
- [ ] CSV export from phone
- [ ] auto-start at login without UAC (scheduled task)
- [ ] multi-GPU and AMD/Intel vendor validation
- [ ] overlay-free Windows toast fallback for exclusive-fullscreen prompts
