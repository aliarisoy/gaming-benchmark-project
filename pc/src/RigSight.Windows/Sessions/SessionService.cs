using System.Diagnostics;
using RigSight.Core.Logging;
using RigSight.Core.Models;
using RigSight.Core.Protocol;
using RigSight.Core.Storage;
using RigSight.Core.Telemetry;
using RigSight.Windows.Games;
using RigSight.Windows.Sensors;
using RigSight.Windows.Transport;

namespace RigSight.Windows.Sessions;

public enum SessionState { Idle, Prompting, Recording }

/// <summary>
/// Owns the prompt → record → finalize state machine. Both the PC UI and the phone
/// act on this service; the PC remains authoritative if the phone disappears.
/// </summary>
public sealed class SessionService : IAsyncDisposable
{
    private readonly ConfigStore _config;
    private readonly SessionStore _store;
    private readonly GameWatcher _watcher;
    private readonly HardwareSensorService _sensors;
    private readonly ITransportHub _hub;
    private readonly FrameTimeStats _frameStats = new();
    private readonly DemoSampleGenerator _demoGenerator = new();
    private readonly Stopwatch _sessionClock = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly List<TelemetrySample> _recent = [];

    private SessionState _state = SessionState.Idle;
    private PromptInfo? _pendingPrompt;
    private GameProfile? _promptGame;
    private int _promptPid;
    private CancellationTokenSource? _promptExpiry;
    private SessionInfo? _activeSession;
    private GameProfile? _activeGame;
    private int _activePid;
    private bool _demo;
    private SessionRecorder? _recorder;
    private PresentMonService? _presentMon;
    private CancellationTokenSource? _samplerCts;
    private Task? _samplerTask;
    private TelemetrySample? _lastSample;
    private bool _disposed;

    public SessionService(
        ConfigStore config,
        SessionStore store,
        GameWatcher watcher,
        HardwareSensorService sensors,
        ITransportHub hub)
    {
        _config = config;
        _store = store;
        _watcher = watcher;
        _sensors = sensors;
        _hub = hub;
        _watcher.GameDetected += OnGameDetected;
        _watcher.GameExited += OnGameExited;
    }

    public SessionState State { get { lock (_gate) return _state; } }
    public SessionInfo? ActiveSession { get { lock (_gate) return _activeSession; } }
    public PromptInfo? PendingPrompt { get { lock (_gate) return _pendingPrompt; } }
    public TelemetrySample? LastSample { get { lock (_gate) return _lastSample?.Clone(); } }
    public bool IsDemo { get { lock (_gate) return _demo; } }
    public HardwareSnapshot LatestHardware => _sensors.Latest;
    public IReadOnlyList<TelemetrySample> RecentSamples { get { lock (_gate) return _recent.ToArray(); } }

    public event Action<PromptInfo>? PromptRequested;
    public event Action<string, string>? PromptClosed;
    public event Action<SessionInfo>? SessionStarted;
    public event Action<SessionSummary>? SessionEnded;
    public event Action<TelemetrySample>? SampleProduced;
    public event Action<SessionState>? StateChanged;
    public event Action<string>? Notice;

    public TransportSnapshot GetSnapshot()
    {
        lock (_gate) return new TransportSnapshot(_activeSession, _pendingPrompt, _lastSample?.Clone());
    }

    private void OnGameDetected(GameLaunch launch) => BeginPrompt(launch);

    private void OnGameExited(GameExit exit)
    {
        int activePid;
        bool demo;
        lock (_gate)
        {
            activePid = _activePid;
            demo = _demo;
        }
        if (demo || activePid != exit.ProcessId) return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            if (!_watcher.IsProcessRunning(exit.ProcessId) && State == SessionState.Recording)
                await StopSessionAsync(StopReasons.GameExit);
        });
    }

    private void BeginPrompt(GameLaunch launch)
    {
        PromptInfo prompt;
        lock (_gate)
        {
            if (_state != SessionState.Idle)
            {
                Notice?.Invoke($"Already tracking {( _activeGame?.Name ?? "a game")}.");
                return;
            }

            _state = SessionState.Prompting;
            _promptGame = launch.Game;
            _promptPid = launch.ProcessId;
            _pendingPrompt = new PromptInfo
            {
                GameId = launch.Game.Id,
                GameName = launch.Game.Name,
                ExeName = launch.Game.ExeName,
                ProcessId = launch.ProcessId,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeMilliseconds(),
            };
            prompt = _pendingPrompt;
        }

        Log.Info($"Prompting for {launch.Game.Name} (pid {launch.ProcessId}).");
        RaiseStateChanged();
        StartPromptExpiry(prompt);
        try { PromptRequested?.Invoke(prompt); } catch (Exception ex) { Log.Error("Prompt handler failed.", ex); }

        if (_config.Current.NotifyPhone)
            _ = BroadcastPromptAsync(prompt);
    }

    private async Task BroadcastPromptAsync(PromptInfo prompt)
    {
        try { await _hub.BroadcastAsync(new WireMessage { Type = MessageTypes.Prompt, Prompt = prompt }); }
        catch (Exception ex) { Log.Warn($"Could not send phone prompt: {ex.Message}"); }
    }

    private void StartPromptExpiry(PromptInfo prompt)
    {
        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            _promptExpiry?.Cancel();
            _promptExpiry?.Dispose();
            _promptExpiry = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), cts.Token);
                await ExpirePromptAsync(prompt.Id);
            }
            catch (OperationCanceledException) { /* acted on */ }
            catch (Exception ex) { Log.Warn($"Prompt expiry failed: {ex.Message}"); }
        });
    }

    private void CancelPromptExpiry()
    {
        lock (_gate)
        {
            try { _promptExpiry?.Cancel(); } catch { /* ignore */ }
            _promptExpiry?.Dispose();
            _promptExpiry = null;
        }
    }

    public async Task<bool> StartFromPromptAsync(string promptId, string source)
    {
        await _transition.WaitAsync();
        try
        {
            PromptInfo? prompt;
            GameProfile? game;
            int pid;
            lock (_gate)
            {
                if (_state != SessionState.Prompting || _pendingPrompt?.Id != promptId)
                    return false;
                prompt = _pendingPrompt;
                game = _promptGame;
                pid = _promptPid;
                ClearPromptLocked();
            }

            if (game is null)
            {
                SetIdle();
                return false;
            }

            if (!_watcher.IsProcessRunning(pid))
            {
                Log.Warn($"Cannot start {game.Name}: process {pid} is gone.");
                WaitForPhoneDismiss(prompt.Id, "expired");
                Notice?.Invoke($"{game.Name} exited before tracking started.");
                SetIdle();
                return false;
            }

            var started = await StartCoreAsync(game, pid, prompt.Id, demo: false);
            if (!started)
            {
                WaitForPhoneDismiss(prompt.Id, "expired");
                SetIdle();
            }
            return started;
        }
        finally
        {
            _transition.Release();
        }
    }

    public async Task<bool> DeclinePromptAsync(string promptId)
    {
        await _transition.WaitAsync();
        try
        {
            int pid;
            lock (_gate)
            {
                if (_state != SessionState.Prompting || _pendingPrompt?.Id != promptId)
                    return false;
                pid = _promptPid;
                ClearPromptLocked();
            }

            _watcher.SuppressProcess(pid);
            WaitForPhoneDismiss(promptId, "declined");
            SetIdle();
            Log.Info($"Prompt {promptId} declined; suppressing pid {pid} until the game exits.");
            return true;
        }
        finally
        {
            _transition.Release();
        }
    }

    private async Task ExpirePromptAsync(string promptId)
    {
        await _transition.WaitAsync();
        try
        {
            int pid;
            lock (_gate)
            {
                if (_state != SessionState.Prompting || _pendingPrompt?.Id != promptId)
                    return;
                pid = _promptPid;
                ClearPromptLocked();
            }

            _watcher.SuppressProcess(pid);
            WaitForPhoneDismiss(promptId, "expired");
            SetIdle();
            Log.Info($"Prompt {promptId} expired.");
        }
        finally
        {
            _transition.Release();
        }
    }

    private void ClearPromptLocked()
    {
        _pendingPrompt = null;
        _promptGame = null;
        _promptPid = 0;
        CancelPromptExpiry();
    }

    private void WaitForPhoneDismiss(string promptId, string reason)
    {
        try { PromptClosed?.Invoke(promptId, reason); } catch { /* ignore */ }
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.BroadcastAsync(new WireMessage
                {
                    Type = MessageTypes.PromptClosed,
                    PromptId = promptId,
                    Reason = reason,
                });
            }
            catch { /* ignore */ }
        });
    }

    private async Task<bool> StartCoreAsync(GameProfile game, int pid, string? promptId, bool demo)
    {
        SessionRecorder recorder;
        try
        {
            recorder = new SessionRecorder(game, _config.Current.SampleIntervalMs);
        }
        catch (Exception ex)
        {
            Log.Error("Could not create session recorder.", ex);
            Notice?.Invoke($"Could not start recording: {ex.Message}");
            return false;
        }

        lock (_gate)
        {
            _recorder = recorder;
            _activeSession = recorder.Info;
            _activeGame = game;
            _activePid = pid;
            _demo = demo;
            _lastSample = null;
            _recent.Clear();
            _state = SessionState.Recording;
        }

        _frameStats.Reset();
        _sessionClock.Restart();
        game.LastPlayedAt = DateTimeOffset.UtcNow;
        _config.Save();

        if (!demo && pid > 0)
        {
            _presentMon = new PresentMonService(_frameStats);
            _presentMon.CaptureError += OnPresentMonError;
            if (!_presentMon.TryStart(pid))
                Notice?.Invoke(_presentMon.LastError ?? "FPS capture is unavailable; hardware metrics are still recording.");
        }

        _samplerCts = new CancellationTokenSource();
        var token = _samplerCts.Token;
        _samplerTask = Task.Run(() => SamplerLoopAsync(recorder, demo, token), token);

        RaiseStateChanged();
        try { SessionStarted?.Invoke(recorder.Info); } catch (Exception ex) { Log.Error("SessionStarted handler failed.", ex); }

        if (promptId is not null) WaitForPhoneDismiss(promptId, "started");

        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.BroadcastAsync(new WireMessage
                {
                    Type = MessageTypes.SessionStarted,
                    Session = recorder.Info,
                });
            }
            catch (Exception ex) { Log.Warn($"Could not broadcast session start: {ex.Message}"); }
        });

        Log.Info($"Session {recorder.Info.Id} started for {game.Name} ({(demo ? "demo" : $"pid {pid}")}).");
        return true;
    }

    private async Task SamplerLoopAsync(SessionRecorder recorder, bool demo, CancellationToken ct)
    {
        var interval = Math.Clamp(_config.Current.SampleIntervalMs, 250, 5000);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (State != SessionState.Recording) break;
                var elapsed = _sessionClock.ElapsedMilliseconds;
                TelemetrySample sample;
                if (demo)
                {
                    sample = _demoGenerator.Next(elapsed);
                }
                else
                {
                    var hw = _sensors.Latest;
                    var frames = _frameStats.Snapshot(Environment.TickCount64);
                    sample = new TelemetrySample
                    {
                        T = elapsed,
                        Fps = frames.Fps,
                        FrameTimeMs = frames.FrameTimeMs,
                        Low1Fps = frames.Low1Fps,
                        Low01Fps = frames.Low01Fps,
                        CpuLoadPct = hw.CpuLoadPct,
                        CpuTempC = hw.CpuTempC,
                        CpuPowerW = hw.CpuPowerW,
                        CpuClockMhz = hw.CpuClockMhz,
                        GpuLoadPct = hw.GpuLoadPct,
                        GpuTempC = hw.GpuTempC,
                        GpuHotspotC = hw.GpuHotspotC,
                        GpuPowerW = hw.GpuPowerW,
                        GpuClockMhz = hw.GpuClockMhz,
                        VramUsedMb = hw.VramUsedMb,
                        VramTotalMb = hw.VramTotalMb,
                        RamUsedMb = hw.RamUsedMb,
                        RamTotalMb = hw.RamTotalMb,
                    };
                }

                lock (_gate)
                {
                    if (_state != SessionState.Recording) break;
                    _lastSample = sample;
                    _recent.Add(sample);
                    if (_recent.Count > 600) _recent.RemoveRange(0, _recent.Count - 600);
                }

                recorder.Append(sample);
                try { SampleProduced?.Invoke(sample); } catch (Exception ex) { Log.Debug($"Sample handler failed: {ex.Message}"); }

                try { await _hub.BroadcastAsync(new WireMessage { Type = MessageTypes.Sample, Sample = sample }); }
                catch (Exception ex) { Log.Debug($"Sample broadcast failed: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (Exception ex)
        {
            Log.Error("Sampler stopped unexpectedly.", ex);
            Notice?.Invoke($"Telemetry stopped: {ex.Message}");
        }
    }

    private void OnPresentMonError(string message)
    {
        Log.Warn($"FPS capture: {message}");
        Notice?.Invoke($"FPS capture unavailable: {message}");
    }

    public async Task<bool> StartForegroundAsync()
    {
        var found = _watcher.FindForeground();
        if (found is null)
        {
            Notice?.Invoke("The foreground app is not in your game library. Add its .exe in Games first.");
            return false;
        }

        using var process = found.Value.Process;
        return await StartDirectAsync(found.Value.Game, process.Id);
    }

    public async Task<bool> StartDirectAsync(GameProfile game, int pid)
    {
        await _transition.WaitAsync();
        try
        {
            lock (_gate)
            {
                if (_state != SessionState.Idle) return false;
            }

            if (!_watcher.IsProcessRunning(pid))
            {
                Notice?.Invoke($"{game.Name} is not running.");
                return false;
            }

            return await StartCoreAsync(game, pid, promptId: null, demo: false);
        }
        finally
        {
            _transition.Release();
        }
    }

    public async Task<bool> StartDemoAsync()
    {
        await _transition.WaitAsync();
        try
        {
            lock (_gate)
            {
                if (_state != SessionState.Idle) return false;
            }

            var demoGame = new GameProfile
            {
                Id = "demo",
                Name = "RigSight Demo",
                ExeName = "RigSight.Demo.exe",
            };
            return await StartCoreAsync(demoGame, 0, promptId: null, demo: true);
        }
        finally
        {
            _transition.Release();
        }
    }

    public async Task StopSessionAsync(string reason)
    {
        await _transition.WaitAsync();
        try
        {
            SessionRecorder? recorder;
            PresentMonService? presentMon;
            Task? sampler;
            CancellationTokenSource? samplerCts;
            SessionInfo? session;
            lock (_gate)
            {
                if (_state != SessionState.Recording) return;
                _state = SessionState.Idle;
                recorder = _recorder;
                presentMon = _presentMon;
                sampler = _samplerTask;
                samplerCts = _samplerCts;
                session = _activeSession;
                _recorder = null;
                _presentMon = null;
                _samplerTask = null;
                _samplerCts = null;
            }

            RaiseStateChanged();
            try { samplerCts?.Cancel(); } catch { /* ignore */ }
            if (sampler is not null)
            {
                try { await Task.WhenAny(sampler, Task.Delay(2500)); } catch { /* ignore */ }
            }
            if (presentMon is not null)
            {
                presentMon.CaptureError -= OnPresentMonError;
                await presentMon.StopAsync();
            }

            if (recorder is null)
            {
                ResetSessionState();
                return;
            }

            var summary = recorder.Complete(
                reason,
                _frameStats.SessionLowFps(1),
                _frameStats.SessionLowFps(0.1),
                _frameStats.SessionAverageFps,
                _frameStats.SessionMedianFps);
            recorder.Dispose();

            try { _store.Save(summary); }
            catch (Exception ex) { Log.Error("Could not index session.", ex); }

            ResetSessionState();
            Log.Info($"Session {summary.Id} ended ({reason}).");
            try { SessionEnded?.Invoke(summary); } catch (Exception ex) { Log.Error("SessionEnded handler failed.", ex); }

            try
            {
                await _hub.BroadcastAsync(new WireMessage
                {
                    Type = MessageTypes.SessionEnded,
                    Summary = summary,
                });
            }
            catch (Exception ex) { Log.Warn($"Could not broadcast session end: {ex.Message}"); }

            samplerCts?.Dispose();
            _ = session;
        }
        finally
        {
            _transition.Release();
        }
    }

    private void ResetSessionState()
    {
        lock (_gate)
        {
            _activeSession = null;
            _activeGame = null;
            _activePid = 0;
            _demo = false;
            _lastSample = null;
            _recent.Clear();
            _state = SessionState.Idle;
        }
        RaiseStateChanged();
    }

    public async Task HandleCommandAsync(WireMessage message, ITransportClient? client)
    {
        try
        {
            switch (message.Type)
            {
                case MessageTypes.StartSession:
                {
                    bool started;
                    if (!string.IsNullOrWhiteSpace(message.PromptId))
                    {
                        started = await StartFromPromptAsync(message.PromptId!, "phone");
                    }
                    else if (!string.IsNullOrWhiteSpace(message.GameId))
                    {
                        var game = _config.Current.Games.FirstOrDefault(g => g.Id == message.GameId);
                        var found = game is null ? null : _watcher.FindRunning(game);
                        if (found is null)
                        {
                            await SendErrorAsync(client, "not_running", "That game is not running.");
                            return;
                        }
                        using var process = found.Value.Process;
                        started = await StartDirectAsync(found.Value.Game, process.Id);
                    }
                    else
                    {
                        await SendErrorAsync(client, "bad_request", "No promptId or gameId supplied.");
                        return;
                    }

                    if (!started)
                        await SendErrorAsync(client, "start_failed", "Could not start the session. The game may have exited.");
                    return;
                }

                case MessageTypes.DeclinePrompt:
                    if (message.PromptId is not null)
                        await DeclinePromptAsync(message.PromptId);
                    return;

                case MessageTypes.StopSession:
                {
                    var session = ActiveSession;
                    if (session is null)
                    {
                        await SendErrorAsync(client, "not_recording", "No session is active.");
                        return;
                    }
                    if (message.SessionId is not null && message.SessionId != session.Id)
                    {
                        await SendErrorAsync(client, "not_found", "That session is not active.");
                        return;
                    }
                    await StopSessionAsync(StopReasons.Phone);
                    return;
                }

                default:
                    await SendErrorAsync(client, "unknown_command", $"Unknown command '{message.Type}'.");
                    return;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Command '{message.Type}' failed.", ex);
            await SendErrorAsync(client, "internal_error", ex.Message);
        }
    }

    private static async Task SendErrorAsync(ITransportClient? client, string code, string message)
    {
        if (client is null) return;
        try
        {
            await client.SendAsync(new WireMessage
            {
                Type = MessageTypes.Error,
                Code = code,
                Message = message,
            });
        }
        catch { /* ignore */ }
    }

    private void SetIdle()
    {
        lock (_gate) _state = SessionState.Idle;
        RaiseStateChanged();
        _ = Task.Run(async () =>
        {
            // Refresh transport snapshots after a prompt is dismissed.
            await Task.CompletedTask;
        });
    }

    private void RaiseStateChanged()
    {
        try { StateChanged?.Invoke(State); } catch { /* ignore */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.GameDetected -= OnGameDetected;
        _watcher.GameExited -= OnGameExited;
        CancelPromptExpiry();
        if (State == SessionState.Recording)
            await StopSessionAsync(StopReasons.AgentShutdown);
        _transition.Dispose();
    }
}
