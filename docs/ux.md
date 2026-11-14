# UX flows and screen inventory

## Principles

- **The game owns the PC screen.** Nothing is drawn over the game, ever.
- **Explicit, reversible start.** A game launch asks; recording starts only on a clear
  action. Starting on either device is equivalent.
- **The phone is a companion, not the recorder.** Closing the app, switching to
  Messages, or locking the screen must not stop the session.
- **Quiet while gaming, rich when reviewing.** Live UI is glanceable; depth lives in
  history.
- **No dead ends.** If Bluetooth is unavailable, Wi-Fi + IP still works. If Wi-Fi is
  unavailable, BLE fallback still works. If neither is available, the PC records.

## PC first run

1. Agent starts elevated; a tray icon appears. Main window opens on first run.
2. Dashboard shows detected hardware and a setup checklist:
   - Sensors ready (CPU/GPU names)
   - PresentMon ready
   - Phone paired
   - Firewall rule active
   - At least one game added
3. **Add game** opens a file picker for the game's `.exe` (or accepts a running app
   from a list). The display name defaults to the file's product name/description.
4. **Pair phone** shows the six-digit code, QR (roadmap), and PC name.

## Game launch prompt

**PC**

A compact card slides into the bottom-right corner of the work area:

```
┌─────────────────────────────────────────┐
│  GAME DETECTED                          │
│  Cyberpunk 2077                         │
│  Start a telemetry session?             │
│  [ Start tracking ]   [ Not now ]       │
└─────────────────────────────────────────┘
```

- Topmost, non-activating, 20 s visible, valid for 60 s.
- If a phone is connected, the same prompt is sent immediately.
- If the PC prompt is hidden by exclusive fullscreen, the phone notification and
  `Ctrl+Alt+G` remain available.
- "Not now" declines this launch; the game is not re-prompted until it restarts.

**Phone notification**

```
RigSight · now
Game detected: Cyberpunk 2077
Tap Start to record on your PC. No overlay.
[ Start ]  [ Not now ]
```

High-importance channel, heads-up, actionable from the lock screen. `Start` starts
the session on the PC even if the PC prompt is no longer visible.

## Live dashboard

Entry points: phone notification **Open live**, app home active-session card, or PC
dashboard (which mirrors the same data).

**Portrait (priority)**

```
┌──────────────────────────────┐
│ ‹  Cyberpunk 2077      Wi-Fi │
│    00:42:17        ● LIVE    │
│ ┌──────────────────────────┐ │
│ │  142 FPS         1% 109  │ │
│ │  ╱╲╱╲╱╲╱╲╱╲╱╲╱╲╱╲╱╲╱╲╱   │ │
│ └──────────────────────────┘ │
│ ┌───────────┐ ┌────────────┐ │
│ │ GPU 71°C  │ │ CPU 67°C   │ │
│ │ 312 W 98% │ │ 96 W 41%   │ │
│ └───────────┘ └────────────┘ │
│ ┌───────────┐ ┌────────────┐ │
│ │ VRAM 7.2G │ │ RAM 18.3G  │ │
│ └───────────┘ └────────────┘ │
│  CPU / GPU load chart  ⌄     │
│  [ History ]   [ ⏹ Stop ]    │
└──────────────────────────────┘
```

- The screen stays awake while the dashboard is open (toggleable).
- Switching apps does **not** stop the session; an ongoing notification shows the
  current FPS and returns to the dashboard.
- If the link drops, a non-blocking banner says "Reconnecting… PC is still recording."
- Landscape reflows to two columns: hero + GPU on the left, CPU + memory + charts on
  the right.
- Values unavailable on this hardware show `—`, not `0`.
- Stop asks for confirmation once ("Stop tracking on PC?").

**PC live dashboard**

The same metrics with a desktop layout: session header, large FPS + frame-time chart,
CPU/GPU cards, transport/device status, and a Stop button. Minimizing closes to tray.

## Phone home

- **Disconnected:** illustration + "Connect to your PC" + Scan / Enter IP manually.
- **Connected, idle:** PC status card, games list ("Start" for a running game is
  disabled; starts happen from prompts), recent sessions, Pair/Devices link.
- **Active session:** pinned card with game, elapsed time, FPS, and Open live.

## Pairing

1. PC → Devices → code shown, 10-minute expiry, Regenerate.
2. Phone → Scan. Nearby PCs appear as `DESKTOP-ABC · Bluetooth LE`.
3. Tap a PC → six-digit code entry. Errors are inline (wrong code, expired, locked).
4. On success: "Paired with DESKTOP-ABC" → auto-connect Wi-Fi if available; otherwise
   "Using Bluetooth (slower updates)".
5. A persistent low-priority notification keeps RigSight connected in the background.
   The user can stop the service from settings.

## History

- PC: Sessions list (game, date, duration, avg FPS, 1% low) → detail with summary
  cards, full-resolution charts, raw JSONL path, CSV export.
- Phone: synced summaries + downsampled chart samples, filter by game. Full-resolution
  samples remain on the PC (tap "Open on PC" for details, roadmap).
- Active sessions appear on the Home screen; History lists finished sessions.
- Empty state: "No sessions yet. Launch a game and accept the prompt."

## Notifications

| Channel | Importance | Behavior |
| --- | --- | --- |
| Game detected | High | Heads-up, Start/Not now, sound/vibrate once, expires after 2 min |
| Session active | Low | Ongoing, current FPS, Open live, Stop; updates at most every 5 s |
| Connection | Low | "RigSight connected to DESKTOP-ABC"; tap to open |
| Session saved | Default | Summary after a session ends; tap for history |

No notification is sent while the user is actively viewing the live screen for
"session saved" (avoid redundant alerts). Prompts are per game launch, never per
sample.

## Edge cases

| Situation | UX |
| --- | --- |
| Phone locked during prompt | Notification appears on lock screen; Start works via service. |
| Phone paired but not running the app | PC cannot reach it. PC prompt still records if accepted. Setup explains keeping the service on. |
| Game launched while another session runs | Ignore; surface a small "already tracking X" toast. |
| Game crashes | Session stops after 5 s; summary saved; phone gets "Session saved". |
| PC sleeps / agent quits | Session finalizes as `interrupted`; phone shows "PC disconnected" and reconnects later. |
| Two phones paired | Both receive prompts; first Start wins; the other receives `prompt_closed`. |
| No Wi-Fi (hotel/guest network) | BLE fallback at 1 Hz; a chip explains reduced rate. |
| PresentMon missing/blocked | FPS card shows `—` with "FPS capture unavailable" detail; no session failure. |
| Non-admin launch | Safe mode banner: "CPU thermals/power need administrator; GPU + FPS active." |

## Copy guidelines

- Say "tracking"/"session", never "benchmark" unless the user is comparing runs.
- Use metric units with context: `312 W`, `71 °C`, `7.2 GB`, `144 FPS`.
- Never imply the app controls fans or overclocks; it is observational.
- Do not promise anti-cheat compatibility. State "no overlay, no injection" and offer
  safe mode.
