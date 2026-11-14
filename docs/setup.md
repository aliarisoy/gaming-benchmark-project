# Development setup

## Requirements

- Windows 11 (validated on current Windows 11 builds)
- .NET 10 SDK
- JDK 17
- Android SDK platform 35 + build-tools 35.0.0 and 34.0.0 (Android Studio optional)
- Node.js 20+ (only for the mock PC)
- NVIDIA GPU for the validated telemetry path
- A Bluetooth LE adapter that supports the peripheral role (Windows:
  `BluetoothAdapter.IsPeripheralRoleSupported`)
- Phone: Android 8.0+ (API 26), target Android 15 (API 35). Physical-device
  validation is still in progress.

The setup script installs the SDKs **user-locally** under
`%LOCALAPPDATA%\RigSight\tools\`; it does not modify machine-wide state or require
admin. Re-running it is safe: existing components are reused.

```powershell
.\scripts\setup-dev.ps1
```

| Component | Location |
| --- | --- |
| .NET 10 SDK | `%LOCALAPPDATA%\RigSight\tools\dotnet` |
| Temurin JDK 17 | `%LOCALAPPDATA%\RigSight\tools\jdk-17` |
| Android SDK + platform-tools | `%LOCALAPPDATA%\RigSight\tools\android-sdk` |
| Gradle 8.9 | `%LOCALAPPDATA%\RigSight\tools\gradle-8.9` |
| PresentMon 2.6.0 | `tools\presentmon\PresentMon.exe` |

The helper scripts use these absolute paths, so you do not need to add anything to
`PATH`.

## Run the PC agent

```powershell
.\scripts\run-agent.ps1
```

The agent requests elevation because LibreHardwareMonitor needs it for CPU package
temperature and power. Without elevation the app runs in **safe mode** (GPU + FPS,
CPU load only). Pass `-NoElevate` to run the agent without UAC.

On first run:

1. Check the setup checklist on the dashboard.
2. **Games -> Add game** and pick a game `.exe`.
3. **Devices -> Pair phone**; leave the window open for the six-digit code.
4. If Windows Firewall asks, allow `RigSight.Agent.exe` on **Private** networks.
   The agent also creates a narrowly scoped inbound rule named
   `RigSight phone stream (TCP <port>)` automatically.

Useful switches:

```powershell
.\scripts\run-agent.ps1 -Demo          # synthetic session, no game needed
.\scripts\run-agent.ps1 -NoBle         # Wi-Fi only (diagnose BLE issues)
.\scripts\run-agent.ps1 -Port 48000    # override the WebSocket port for this run
.\scripts\run-agent.ps1 -NoBuild       # start the existing build
```

The same flags exist on the executable (`--demo`, `--no-ble`, `--port`); the script
is just a build-and-launch wrapper.

## Shortcuts

Create a Desktop shortcut and a Start Menu entry:

```powershell
.\scripts\install-shortcuts.ps1
```

Both launch the built agent and show the normal UAC prompt, because the executable
requests administrator rights for full CPU sensors. The Start Menu entry can be
pinned to Start or the taskbar. Remove both with:

```powershell
.\scripts\install-shortcuts.ps1 -Remove
```

The shortcuts point at the current build output (`Debug` by default; pass
`-Configuration Release` for a Release build). Re-run the script after moving the
repository or switching configurations.

The agent is a tray app: closing the main window hides it to the notification area
(look for the mint **R**, possibly behind the **^** overflow). Double-click the icon
to reopen it. Use **Exit RigSight** in its right-click menu to quit.

Logs: `%LOCALAPPDATA%\RigSight\logs\agent-YYYYMMDD.log`.

## Build the Android app

```powershell
.\scripts\build-android.ps1
```

Output: `artifacts\RigSight-debug.apk`. Release builds are unsigned and land at
`artifacts\RigSight-release-unsigned.apk`:

```powershell
.\scripts\build-android.ps1 -Configuration Release
```

The script runs Android lint unless you pass `-SkipLint`.

Install on a USB-connected phone with developer options enabled:

```powershell
.\scripts\build-android.ps1 -Install
```

Or install an existing APK manually:

```powershell
& "$env:LOCALAPPDATA\RigSight\tools\android-sdk\platform-tools\adb.exe" install -r .\artifacts\RigSight-debug.apk
```

Or copy the APK to the phone and open it. Allow installs from the file manager.

### Phone permissions

- Nearby devices (Bluetooth scan/connect) — required for pairing and prompts.
- Notifications — required for game prompts and the ongoing session notification.
- Battery optimization — RigSight asks you to exempt it so the foreground service
  survives aggressive OEM battery managers. This is optional
  but recommended.

The app starts its foreground service once a PC is paired or a connection is
established, and keeps it across app switches. If the process is force-stopped by
the OS or the user, the PC can still record, but phone prompts are unavailable until
the app is reopened.

## PresentMon

The setup script downloads Intel PresentMon 2.6.0 (MIT) to
`tools\presentmon\PresentMon.exe`. The agent copies it next to the executable at
build time. If it is missing, FPS capture is disabled but hardware telemetry still
works.

PresentMon needs ETW access. Running the agent elevated satisfies this. Alternatively
add your user to the **Performance Log Users** group and run the agent unelevated
(CPU sensors may still require elevation).

## PresentMon and anti-cheat

PresentMon is not an overlay and does not inject into game processes. It consumes
OS-level DXGI/D3D ETW events. Full sensor mode additionally loads
LibreHardwareMonitor's kernel driver for CPU telemetry; strict kernel anti-cheat
titles may block or flag it. Safe mode avoids the driver.

RigSight never draws over the game and does not alter game files.

## Ports and firewall

| Port | Protocol | Purpose |
| --- | --- | --- |
| 47823 (default) | TCP/WebSocket | LAN telemetry + control |

The port is configurable in the agent's Settings (or with `-Port` / `--port`). The
phone's manual IP screen must use the same port. A dev config may use a different
port, for example 48000.

The agent binds to all LAN interfaces and requires the bearer token. Never expose the
stream port to the internet. Each port gets its own firewall rule named
`RigSight phone stream (TCP <port>)`. To remove all of them:

```powershell
Get-NetFirewallRule -DisplayName 'RigSight phone stream*' | Remove-NetFirewallRule
```

## Tests

```powershell
# .NET build and tests (user-local SDK)
$dotnet = "$env:LOCALAPPDATA\RigSight\tools\dotnet\dotnet.exe"
& $dotnet build .\pc\RigSight.sln
& $dotnet test .\pc\tests\RigSight.Core.Tests\RigSight.Core.Tests.csproj

# Android build + lint
.\scripts\build-android.ps1

# Mock PC protocol smoke test (no sensors, no elevation)
node .\scripts\mock-pc.mjs --prompt
```

## Mock PC

`scripts/mock-pc.mjs` is a dependency-free WebSocket server implementing protocol
v1. It seeds two history sessions, streams synthetic samples at 2 Hz, and handles
prompts and session control. Use it to test the phone without the PC agent.

```powershell
node .\scripts\mock-pc.mjs                       # history + manual start
node .\scripts\mock-pc.mjs --prompt              # prompt after the phone connects
node .\scripts\mock-pc.mjs --auto --duration 30  # start and stop on its own
node .\scripts\mock-pc.mjs --port 48000          # match a custom phone port
node .\scripts\mock-pc.mjs --open                # no code/token (dev only)
```

Defaults: pairing code `123456`, token `dev-token`, PC name `MOCK-RIG`. On the
phone, connect by IP with the printed LAN address. The first connection needs the
code; the phone stores the token for reconnects. `--help` lists every option. If
Windows Firewall prompts for Node.js, allow it on **Private** networks.

## On-device smoke test

Run these on the physical phone before calling a build validated:

1. Install and launch the app; grant notifications and Nearby devices.
2. Connect by IP with a fresh six-digit code.
3. Trigger a prompt (launch a tracked game or use the mock PC); tap **Start**.
4. Lock the phone, wait, unlock: the PC must keep recording and the live view must
   catch up.
5. Switch to another app and back; the foreground service and stream must survive.
6. Stop the session; confirm the summary appears in History and charts open.
7. Relaunch the app and confirm auto-connect; forget the PC and re-pair.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| No CPU temp/power | Run elevated; check log for LHM driver errors; some anti-cheat blocks the driver. |
| FPS shows `—` | PresentMon exists, agent elevated, game actually presents frames; check `logs`. |
| Phone can't find PC | Bluetooth on; PC adapter supports LE peripheral; phone location permission (Android 11 and below); try Scan again. |
| Phone connects to BLE but no Wi-Fi | Same subnet? AP/client isolation off? Use manual IP in the app; fall back to BLE. |
| Phone shows `unauthorized` / drops after connecting | Token revoked or PC data reset; Forget PC on the phone and pair again. |
| Phone can't connect by IP | Same port on both sides; firewall rule `RigSight phone stream (TCP <port>)`; agent log shows the bound port. |
| Prompt doesn't appear over a game | Exclusive fullscreen; use `Ctrl+Alt+G` or the phone notification. |
| Prompt arrives late | OEM battery optimization killed the service; exempt RigSight. The PC still records. |
