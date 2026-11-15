using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RigSight.Core.Logging;
using RigSight.Core.Models;
using RigSight.Core.Storage;
using RigSight.Windows.Sensors;
using RigSight.Windows.Sessions;

namespace RigSight.Agent.ViewModels;

public enum AppPage { Dashboard, Games, Sessions, Devices, Settings }

/// <summary>The single desktop view model. Owns dashboard state and user commands.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AgentHost _host;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _noticeTimer;
    private readonly List<double?> _fpsSeries = [];
    private readonly List<double?> _lowSeries = [];
    private readonly HashSet<string> _notifiedErrors = [];
    private DateTimeOffset _sessionStartedAt;
    private PromptInfo? _pendingPrompt;
    private int _tick;
    private bool _disposed;

    public MainViewModel(AgentHost host)
    {
        _host = host;
        _dispatcher = Dispatcher.CurrentDispatcher;

        NavItems =
        [
            new NavItem("\uE80F", "Dashboard"),
            new NavItem("\uE7FC", "Games"),
            new NavItem("\uE81C", "Sessions"),
            new NavItem("\uE8EA", "Devices"),
            new NavItem("\uE713", "Settings"),
        ];

        SampleIntervalOptions =
        [
            new SampleIntervalOption(250, "0.25 s"),
            new SampleIntervalOption(500, "0.5 s"),
            new SampleIntervalOption(1000, "1 s"),
        ];

        _host.Sessions.PromptRequested += OnPromptRequested;
        _host.Sessions.PromptClosed += OnPromptClosed;
        _host.Sessions.SessionStarted += OnSessionStarted;
        _host.Sessions.SessionEnded += OnSessionEnded;
        _host.Sessions.SampleProduced += OnSampleProduced;
        _host.Sessions.StateChanged += OnStateChanged;
        _host.Sessions.RecordingPausedChanged += OnRecordingPausedChanged;
        _host.Sessions.Notice += OnServiceNotice;
        _host.Hub.StatusChanged += OnTransportChanged;
        Log.LineWritten += OnLogLine;

        PcName = _host.Config.Current.PcName;
        VersionText = $"RigSight {BuildInfo.Version} · protocol {BuildInfo.ProtocolVersion}";
        DataFolder = RigSightPaths.Root;
        ElevationText = Environment.IsPrivilegedProcess ? "Administrator" : "Safe mode · standard user";
        FpsCaptureAvailable = _host.PresentMonAvailable;
        FpsStatusText = FpsCaptureAvailable ? "PresentMon ready" : "PresentMon missing";

        LoadSettings();
        RefreshGames();
        RefreshSessions();
        RefreshDevices();
        UpdatePairing();
        UpdateTransport();
        UpdateHardware(_host.Sensors.Latest);
        UpdateStatusSummary();

        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            HasNotice = false;
        };

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += OnClockTick;
        _clock.Start();

        AddActivity(DateTimeOffset.Now, $"RigSight {BuildInfo.Version} ready on {PcName}.", false, false);
        var hardware = _host.Sensors.Latest;
        if (!hardware.FullSensors && hardware.Warning is { } warning)
            ShowNotice(warning);
    }

    public IReadOnlyList<NavItem> NavItems { get; }
    public IReadOnlyList<SampleIntervalOption> SampleIntervalOptions { get; }
    public ObservableCollection<GameRow> Games { get; } = [];
    public ObservableCollection<SessionRow> Sessions { get; } = [];
    public ObservableCollection<DeviceRow> Devices { get; } = [];
    public ObservableCollection<ActivityItem> Activity { get; } = [];

    public event Action<PromptInfo>? PromptRequested;
    public event Action? PromptDismissed;
    public event Action? ShowRequested;
    public event Action? ExitRequested;

    // Navigation & chrome
    [ObservableProperty] private int _pageIndex;
    [ObservableProperty] private string _pcName = "";
    [ObservableProperty] private string _versionText = "";
    [ObservableProperty] private string _elevationText = "";
    [ObservableProperty] private string _dataFolder = "";
    [ObservableProperty] private string _statusSummary = "";
    [ObservableProperty] private string _trayStatus = "RigSight · idle";
    [ObservableProperty] private string _noticeText = "";
    [ObservableProperty] private bool _hasNotice;

    // Session hero
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isRecordingPaused;
    [ObservableProperty] private bool _isPrompting;
    [ObservableProperty] private string _sessionGame = "No active session";
    [ObservableProperty] private string _sessionSubtitle = "Games in your library ask before tracking.";
    [ObservableProperty] private string _sessionElapsed = "00:00";
    [ObservableProperty] private string _sessionStateLabel = "IDLE";
    [ObservableProperty] private string _lastSessionText = "No sessions yet. Launch a game and accept the prompt.";
    [ObservableProperty] private string _liveFpsValue = "—";
    [ObservableProperty] private string _frameTimeValue = "—";
    [ObservableProperty] private string _low1Value = "—";
    [ObservableProperty] private string _low01Value = "—";

    // Metrics
    [ObservableProperty] private string _gpuTempValue = "—";
    [ObservableProperty] private string _gpuLoadValue = "—";
    [ObservableProperty] private string _gpuPowerValue = "—";
    [ObservableProperty] private string _gpuClockValue = "—";
    [ObservableProperty] private string _cpuTempValue = "—";
    [ObservableProperty] private string _cpuLoadValue = "—";
    [ObservableProperty] private string _cpuPowerValue = "—";
    [ObservableProperty] private string _cpuClockValue = "—";
    [ObservableProperty] private string _vramText = "—";
    [ObservableProperty] private string _ramText = "—";
    [ObservableProperty] private string _cpuName = "CPU";
    [ObservableProperty] private string _gpuName = "GPU";
    [ObservableProperty] private string _sensorMode = "Sensors";
    [ObservableProperty] private string _sensorWarning = "";
    [ObservableProperty] private bool _fullSensors;
    [ObservableProperty] private bool _gpuAvailable;
    [ObservableProperty] private bool _fpsCaptureAvailable;
    [ObservableProperty] private string _fpsStatusText = "";

    // Transport & pairing
    [ObservableProperty] private string _transportLabel = "OFFLINE";
    [ObservableProperty] private string _transportDetail = "";
    [ObservableProperty] private string _transportError = "";
    [ObservableProperty] private string _clientCountText = "No phone connected";
    [ObservableProperty] private string _wsStatusText = "";
    [ObservableProperty] private string _wsModeChip = "2 HZ STREAM";
    [ObservableProperty] private bool _wsLanAccessible;
    [ObservableProperty] private string _bleStatusText = "";
    [ObservableProperty] private bool _wsRunning;
    [ObservableProperty] private bool _bleRunning;
    [ObservableProperty] private string _pairingCode = "";
    [ObservableProperty] private string _pairingCodeRaw = "";
    [ObservableProperty] private string _pairingExpiryText = "";
    [ObservableProperty] private string _pairingNote = "";
    [ObservableProperty] private IReadOnlyList<string> _wsUrls = [];

    // Collection state
    [ObservableProperty] private bool _hasGames;
    [ObservableProperty] private bool _hasSessions;
    [ObservableProperty] private bool _hasDevices;
    [ObservableProperty] private bool _showSetup;
    [ObservableProperty] private string _gameCountText = "0 games";
    [ObservableProperty] private string _sessionCountText = "0 sessions";
    [ObservableProperty] private string _deviceCountText = "0 phones";

    // Charts
    [ObservableProperty] private IReadOnlyList<double?> _fpsSeriesValues = [];
    [ObservableProperty] private IReadOnlyList<double?> _lowSeriesValues = [];
    [ObservableProperty] private SessionRow? _selectedSession;
    [ObservableProperty] private bool _hasSelectedSession;
    [ObservableProperty] private bool _selectedSessionLoading;
    [ObservableProperty] private string _selectedChartCaption = "";
    [ObservableProperty] private IReadOnlyList<double?> _selectedFpsSeries = [];
    [ObservableProperty] private IReadOnlyList<double?> _selectedLowSeries = [];

    // Settings
    [ObservableProperty] private string _pcDisplayName = "";
    [ObservableProperty] private string _wsPortText = "47823";
    [ObservableProperty] private bool _notifyPhone = true;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _autoStartAtLogin;
    [ObservableProperty] private SampleIntervalOption? _selectedInterval;
    [ObservableProperty] private string _settingsNote = "";

    public AppPage Page => (AppPage)PageIndex;
    public bool IsIdle => !IsRecording && !IsPrompting;
    public bool CanStartSession => !IsRecording && !IsPrompting;
    public bool HasTransportError => !string.IsNullOrWhiteSpace(TransportError);
    public PromptInfo? PendingPrompt => _pendingPrompt;

    public Task<bool> StartPromptAsync(PromptInfo prompt) => _host.Sessions.StartFromPromptAsync(prompt.Id, "pc");
    public Task<bool> DeclinePromptAsync(PromptInfo prompt) => _host.Sessions.DeclinePromptAsync(prompt.Id);
    public void RequestShowWindow() => ShowRequested?.Invoke();

    public void ShowPairing()
    {
        PageIndex = (int)AppPage.Devices;
        RefreshDevices();
        UpdatePairing();
        ShowRequested?.Invoke();
    }

    public Task RunStartForegroundAsync() => StartForegroundAsync();
    public Task RunStopAsync() => StopSessionAsync();

    private void LoadSettings()
    {
        var config = _host.Config.Current;
        PcDisplayName = config.DisplayName ?? config.PcName;
        WsPortText = config.WsPort.ToString(CultureInfo.InvariantCulture);
        NotifyPhone = config.NotifyPhone;
        StartMinimized = config.StartMinimized;
        AutoStartAtLogin = config.AutoStartAtLogin;
        SelectedInterval = SampleIntervalOptions.FirstOrDefault(o => o.Value == config.SampleIntervalMs)
                           ?? SampleIntervalOptions[1];
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        _tick++;
        var hardware = _host.Sensors.Latest;
        UpdateHardware(hardware);
        if (IsRecording)
        {
            var elapsed = DateTimeOffset.UtcNow - _sessionStartedAt;
            SessionElapsed = Format.Elapsed(elapsed);
        }

        if (_tick % 2 == 0)
        {
            UpdatePairing();
            UpdateRunningGames();
        }

        if (_tick % 3 == 0)
            UpdateTransport();
    }

    // ── Session events ──────────────────────────────────────────────────────

    private void OnPromptRequested(PromptInfo prompt) => Post(() =>
    {
        _pendingPrompt = prompt;
        IsPrompting = true;
        SessionStateLabel = "PROMPT";
        SessionGame = prompt.GameName;
        SessionSubtitle = "Waiting for confirmation on the PC or phone.";
        SessionElapsed = "00:00";
        UpdateStatusSummary();
        PromptRequested?.Invoke(prompt);
    });

    private void OnPromptClosed(string promptId, string reason) => Post(() =>
    {
        if (_pendingPrompt?.Id != promptId) return;
        _pendingPrompt = null;
        IsPrompting = _host.Sessions.State == SessionState.Prompting;
        if (!IsRecording)
        {
            SessionGame = "No active session";
            SessionSubtitle = reason switch
            {
                "declined" => "Prompt declined. The game will not ask again until it restarts.",
                "expired" => "Prompt expired. Start tracking from the Games tab if you still want it.",
                _ => "Games in your library ask before tracking.",
            };
            SessionStateLabel = "IDLE";
        }
        UpdateStatusSummary();
        PromptDismissed?.Invoke();
    });

    private void OnSessionStarted(SessionInfo info) => Post(() =>
    {
        IsRecording = true;
        IsRecordingPaused = false;
        IsPrompting = false;
        _pendingPrompt = null;
        _sessionStartedAt = info.StartedAt;
        SessionGame = info.GameName;
        SessionSubtitle = info.ExeName;
        SessionElapsed = "00:00";
        SessionStateLabel = _host.Sessions.IsDemo ? "DEMO" : "LIVE";
        _fpsSeries.Clear();
        _lowSeries.Clear();
        FpsSeriesValues = [];
        LowSeriesValues = [];
        LiveFpsValue = "—";
        FrameTimeValue = "—";
        Low1Value = "—";
        Low01Value = "—";
        TrayStatus = $"{info.GameName} · starting";
        RefreshGames();
        UpdateStatusSummary();
        PromptDismissed?.Invoke();
        ShowNotice($"Tracking {info.GameName}. The game keeps the screen; no overlay.");
    });

    private void OnSampleProduced(TelemetrySample sample) => Post(() =>
    {
        if (!IsRecording) return;

        LiveFpsValue = Format.Number(sample.Fps);
        FrameTimeValue = Format.Number(sample.FrameTimeMs, "0.0");
        Low1Value = Format.Number(sample.Low1Fps);
        Low01Value = Format.Number(sample.Low01Fps, "0.0");
        GpuTempValue = Format.Number(sample.GpuTempC);
        GpuLoadValue = Format.Number(sample.GpuLoadPct);
        GpuPowerValue = Format.Number(sample.GpuPowerW);
        GpuClockValue = Format.Number(sample.GpuClockMhz);
        CpuTempValue = Format.Number(sample.CpuTempC);
        CpuLoadValue = Format.Number(sample.CpuLoadPct);
        CpuPowerValue = Format.Number(sample.CpuPowerW);
        CpuClockValue = Format.Number(sample.CpuClockMhz);
        VramText = Format.Memory(sample.VramUsedMb, sample.VramTotalMb);
        RamText = Format.Memory(sample.RamUsedMb, sample.RamTotalMb);

        _fpsSeries.Add(sample.Fps);
        _lowSeries.Add(sample.Low1Fps ?? sample.Low01Fps);
        if (_fpsSeries.Count > 240) _fpsSeries.RemoveRange(0, _fpsSeries.Count - 240);
        if (_lowSeries.Count > 240) _lowSeries.RemoveRange(0, _lowSeries.Count - 240);
        FpsSeriesValues = _fpsSeries.ToArray();
        LowSeriesValues = _lowSeries.ToArray();

        TrayStatus = $"{SessionGame} · {LiveFpsValue} FPS";
    });

    private void OnSessionEnded(SessionSummary summary) => Post(() =>
    {
        IsRecording = false;
        IsRecordingPaused = false;
        IsPrompting = false;
        SessionStateLabel = "SAVED";
        LastSessionText = $"{summary.GameName} · {summary.DurationLabel} · {Format.Number(summary.AvgFps)} avg FPS";
        TrayStatus = "RigSight · idle";
        RefreshSessions();
        RefreshGames();
        UpdateStatusSummary();
        ShowNotice($"Session saved · {summary.GameName} · {summary.DurationLabel}.");
    });

    private void OnStateChanged(SessionState state) => Post(() =>
    {
        IsPrompting = state == SessionState.Prompting;
        switch (state)
        {
            case SessionState.Idle when !IsRecording:
                SessionStateLabel = "IDLE";
                break;
            case SessionState.Prompting:
                SessionStateLabel = "PROMPT";
                break;
        }
        UpdateStatusSummary();
    });

    private void OnRecordingPausedChanged(bool paused) => Post(() =>
    {
        if (!IsRecording)
        {
            IsRecordingPaused = false;
            return;
        }

        IsRecordingPaused = paused;
        if (paused)
        {
            SessionStateLabel = "PAUSED";
            SessionSubtitle = "Game minimized or in the background · recording paused";
            TrayStatus = $"{SessionGame} · paused";
        }
        else
        {
            SessionStateLabel = _host.Sessions.IsDemo ? "DEMO" : "LIVE";
            SessionSubtitle = _host.Sessions.ActiveSession?.ExeName ?? "";
            TrayStatus = $"{SessionGame} · {LiveFpsValue} FPS";
        }
        UpdateStatusSummary();
    });

    private void OnServiceNotice(string message) => Post(() => ShowNotice(message));

    private void OnTransportChanged() => Post(UpdateTransport);

    private void OnLogLine(string line)
    {
        var close = line.IndexOf(']');
        if (close < 0 || line.Length < close + 3) return;
        var level = line[..close].TrimStart('[').Trim();
        if (level.Equals("DEBUG", StringComparison.OrdinalIgnoreCase)) return;
        var message = line[(close + 1)..].Trim();
        var timeText = line.Length >= 19 ? line[11..19] : DateTime.Now.ToString("HH:mm:ss");
        Post(() => AddActivity(DateTimeOffset.Now, message, level == "WARN", level == "ERROR", timeText));
    }

    private void AddActivity(DateTimeOffset time, string message, bool warning, bool error, string? timeOverride = null)
    {
        Activity.Insert(0, new ActivityItem(timeOverride ?? time.ToString("HH:mm:ss"), message, warning, error));
        while (Activity.Count > 100) Activity.RemoveAt(Activity.Count - 1);
    }

    private void ShowNotice(string message)
    {
        NoticeText = message;
        HasNotice = true;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    [RelayCommand]
    private void DismissNotice() => HasNotice = false;

    private void Post(Action action)
    {
        if (_disposed) return;
        try
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.BeginInvoke(action);
        }
        catch
        {
            // The window is shutting down.
        }
    }

    // ── State refresh ──────────────────────────────────────────────────────

    private void UpdateHardware(HardwareSnapshot hw)
    {
        CpuName = hw.CpuName ?? CpuName;
        GpuName = hw.GpuName ?? GpuName;
        FullSensors = hw.FullSensors;
        GpuAvailable = hw.GpuAvailable;
        SensorMode = hw.FullSensors ? "FULL SENSORS" : "SAFE MODE";
        SensorWarning = hw.Warning ?? "";
        if (!IsRecording || IsRecordingPaused)
        {
            GpuTempValue = Format.Number(hw.GpuTempC);
            GpuLoadValue = Format.Number(hw.GpuLoadPct);
            GpuPowerValue = Format.Number(hw.GpuPowerW);
            GpuClockValue = Format.Number(hw.GpuClockMhz);
            CpuTempValue = Format.Number(hw.CpuTempC);
            CpuLoadValue = Format.Number(hw.CpuLoadPct);
            CpuPowerValue = Format.Number(hw.CpuPowerW);
            CpuClockValue = Format.Number(hw.CpuClockMhz);
            VramText = Format.Memory(hw.VramUsedMb, hw.VramTotalMb);
            RamText = Format.Memory(hw.RamUsedMb, hw.RamTotalMb);
        }
    }

    private void UpdatePairing()
    {
        var code = _host.PairingSession.Code;
        PairingCode = code.Length == 6 ? $"{code[..3]} {code[3..]}" : code;
        PairingCodeRaw = code;
        var remaining = _host.PairingSession.ExpiresAt - DateTimeOffset.UtcNow;
        PairingExpiryText = _host.PairingSession.IsExpired || remaining <= TimeSpan.Zero
            ? "Expired or locked · regenerate"
            : $"Expires in {remaining.Minutes}:{remaining.Seconds:00}";
        PairingNote = _host.Config.Current.PairedDevices.Count == 0
            ? "Open RigSight on your phone and choose Bluetooth scan, or enter this PC's Wi-Fi address."
            : "Generate a new code only when pairing another phone.";
    }

    private void UpdateTransport()
    {
        var status = _host.Hub.Status;
        WsRunning = status.WebSocketRunning;
        WsLanAccessible = status.WebSocketLanAccessible;
        BleRunning = status.BleRunning;
        WsUrls = status.WsUrls;
        TransportDetail = status.WsUrls.FirstOrDefault() ?? $"ws://localhost:{_host.Config.Current.WsPort}/v1/stream";
        WsModeChip = status.WebSocketLanAccessible ? "2 HZ STREAM" : "LOCAL ONLY";
        WsStatusText = !status.WebSocketRunning
            ? "Wi-Fi stream stopped"
            : status.WebSocketLanAccessible
                ? $"Wi-Fi stream on port {_host.Config.Current.WsPort}"
                : "This PC only · run as administrator";
        BleStatusText = status.BleRunning ? "Bluetooth peripheral advertising" : "Bluetooth unavailable";
        var clients = status.WebSocketClients + status.BleClients;
        ClientCountText = clients == 0
            ? "No phone connected"
            : $"{clients} phone{(clients == 1 ? "" : "s")} connected";
        TransportLabel = status.WebSocketClients > 0
            ? "WI-FI"
            : status.BleClients > 0
                ? "BLUETOOTH"
                : status.WebSocketRunning
                    ? status.WebSocketLanAccessible ? "READY" : "LOCAL ONLY"
                    : "OFFLINE";
        TransportError = status.LastError ?? "";
        OnPropertyChanged(nameof(HasTransportError));
        if (!string.IsNullOrWhiteSpace(TransportError) &&
            _notifiedErrors.Add(TransportError))
            ShowNotice($"Transport: {TransportError}");
        UpdateStatusSummary();
    }

    private void UpdateStatusSummary()
    {
        var transport = WsRunning ? (WsLanAccessible ? "Wi-Fi" : "Wi-Fi local only") : "offline";
        if (BleRunning) transport += " + BLE";
        var mode = IsRecording
            ? IsRecordingPaused ? "Paused" : SessionStateLabel
            : IsPrompting ? "Prompt open" : "Idle";
        StatusSummary = $"{mode} · {transport} · {ClientCountText.ToLowerInvariant()}";
        if (!string.IsNullOrWhiteSpace(TransportError)) StatusSummary = $"Transport issue · {TransportError}";
    }

    private void UpdateRunningGames()
    {
        var running = _host.Watcher.RunningGameIds;
        foreach (var row in Games)
            row.IsRunning = running.Contains(row.Id);
    }

    private void RefreshGames()
    {
        Games.Clear();
        foreach (var game in _host.Config.Current.Games
                     .OrderByDescending(g => g.LastPlayedAt ?? DateTimeOffset.MinValue)
                     .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
            Games.Add(new GameRow(game) { IsRunning = _host.Watcher.RunningGameIds.Contains(game.Id) });

        HasGames = Games.Count > 0;
        ShowSetup = !HasGames;
        GameCountText = Games.Count == 1 ? "1 game" : $"{Games.Count} games";
        UpdateRunningGames();
    }

    public void RefreshSessions()
    {
        var selectedId = SelectedSession?.Id;
        Sessions.Clear();
        foreach (var summary in _host.Store.GetAll(200))
            Sessions.Add(new SessionRow(summary));
        HasSessions = Sessions.Count > 0;
        SessionCountText = Sessions.Count == 1 ? "1 session" : $"{Sessions.Count} sessions";
        SelectedSession = selectedId is not null
            ? Sessions.FirstOrDefault(s => s.Id == selectedId) ?? Sessions.FirstOrDefault()
            : Sessions.FirstOrDefault();
    }

    private void RefreshDevices()
    {
        Devices.Clear();
        foreach (var device in _host.Config.Current.PairedDevices
                     .OrderByDescending(d => d.LastSeenAt ?? d.PairedAt))
            Devices.Add(new DeviceRow(device));
        HasDevices = Devices.Count > 0;
        DeviceCountText = Devices.Count == 1 ? "1 phone" : $"{Devices.Count} phones";
    }

    partial void OnSelectedSessionChanged(SessionRow? value)
    {
        HasSelectedSession = value is not null;
        if (value is null)
        {
            SelectedFpsSeries = [];
            SelectedLowSeries = [];
            SelectedChartCaption = "";
            return;
        }
        _ = LoadSessionSamplesAsync(value);
    }

    private async Task LoadSessionSamplesAsync(SessionRow row)
    {
        SelectedSessionLoading = true;
        try
        {
            var samples = await Task.Run(() => SessionRecorder.ReadSamples(row.Id, 320));
            if (SelectedSession?.Id != row.Id) return;
            SelectedFpsSeries = samples.Select(s => s.Fps).ToArray();
            SelectedLowSeries = samples.Select(s => s.Low1Fps ?? s.Low01Fps).ToArray();
            SelectedChartCaption = samples.Count == 0
                ? "No raw samples on disk"
                : $"{samples.Count} samples · downsampled for preview";
        }
        catch (Exception ex)
        {
            if (SelectedSession?.Id == row.Id)
            {
                SelectedFpsSeries = [];
                SelectedLowSeries = [];
                SelectedChartCaption = $"Could not read samples: {ex.Message}";
            }
        }
        finally
        {
            if (SelectedSession?.Id == row.Id) SelectedSessionLoading = false;
        }
    }

    partial void OnPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(Page));
        if (Page == AppPage.Sessions) RefreshSessions();
        if (Page == AppPage.Devices)
        {
            RefreshDevices();
            UpdatePairing();
        }
        if (Page == AppPage.Games) RefreshGames();
    }

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanStartSession));
    }

    partial void OnIsPromptingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanStartSession));
    }

    // ── Commands: sessions ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task StartForegroundAsync()
    {
        if (!await _host.Sessions.StartForegroundAsync())
            UpdateStatusSummary();
    }

    [RelayCommand]
    private async Task StartGameAsync(GameRow? row)
    {
        if (row is null) return;
        var found = _host.Watcher.FindRunning(row.Game);
        if (found is null)
        {
            ShowNotice($"{row.Name} is not running. Launch it first.");
            return;
        }
        using var process = found.Value.Process;
        await _host.Sessions.StartDirectAsync(row.Game, process.Id);
    }

    [RelayCommand]
    private async Task StartDemoAsync()
    {
        if (!await _host.Sessions.StartDemoAsync())
            ShowNotice("A session is already active.");
    }

    [RelayCommand]
    private async Task StopSessionAsync()
    {
        await _host.Sessions.StopSessionAsync(StopReasons.User);
    }

    // ── Commands: library ───────────────────────────────────────────────────

    [RelayCommand]
    private void AddGame()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add a game to RigSight",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;

        var added = _host.AddGames(dialog.FileNames);
        RefreshGames();
        ShowNotice(added > 0
            ? $"Added {added} game{(added == 1 ? "" : "s")}. RigSight will prompt the next time {(added == 1 ? "it starts" : "they start")}."
            : "Nothing added. Those games are already in the library.");
    }

    [RelayCommand]
    private void RemoveGame(GameRow? row)
    {
        if (row is null) return;
        var answer = MessageBox.Show(
            $"Remove {row.Name} from the library?\n\nRecorded sessions stay in history.",
            "Remove game",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        _host.RemoveGame(row.Id);
        RefreshGames();
        ShowNotice($"{row.Name} removed from the library.");
    }

    // ── Commands: devices & pairing ─────────────────────────────────────────

    [RelayCommand]
    private void RegeneratePairingCode()
    {
        _host.PairingSession.Regenerate();
        UpdatePairing();
        ShowNotice("New pairing code generated. The previous code no longer works.");
    }

    [RelayCommand]
    private void CopyPairingCode()
    {
        try
        {
            Clipboard.SetText(PairingCodeRaw);
            ShowNotice("Pairing code copied.");
        }
        catch (Exception ex)
        {
            ShowNotice($"Could not copy: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RevokeDevice(DeviceRow? row)
    {
        if (row is null) return;
        var answer = MessageBox.Show(
            $"Revoke access for {row.Name}?\n\nIt will need the current pairing code to reconnect.",
            "Revoke phone",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        _host.RevokeDevice(row.Id);
        RefreshDevices();
        UpdatePairing();
        ShowNotice($"{row.Name} revoked.");
    }

    [RelayCommand]
    private void RevokeAllDevices()
    {
        if (!HasDevices) return;
        var answer = MessageBox.Show(
            "Revoke every paired phone? They will all need a new code.",
            "Revoke all phones",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        _host.RevokeAllDevices();
        RefreshDevices();
        UpdatePairing();
        ShowNotice("All paired phones revoked.");
    }

    [RelayCommand]
    private void OpenPairing() => ShowPairing();

    // ── Commands: history ───────────────────────────────────────────────────

    [RelayCommand]
    private void ExportSession(SessionRow? row)
    {
        row ??= SelectedSession;
        if (row is null) return;
        try
        {
            var path = SessionRecorder.ExportCsv(row.Id);
            ShowNotice($"Exported CSV to {path}");
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowNotice($"Export failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenSessionFolder(SessionRow? row)
    {
        row ??= SelectedSession;
        if (row is null) return;
        OpenFolder(Path.Combine(RigSightPaths.SessionsDirectory, row.Id));
    }

    [RelayCommand]
    private void DeleteSession(SessionRow? row)
    {
        row ??= SelectedSession;
        if (row is null) return;
        var answer = MessageBox.Show(
            $"Delete {row.GameName} from {row.DateLabel}?\n\nThis removes the summary and raw samples from this PC.",
            "Delete session",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            _host.Store.Delete(row.Id);
            SessionRecorder.DeleteFiles(row.Id);
            RefreshSessions();
            ShowNotice("Session deleted.");
        }
        catch (Exception ex)
        {
            ShowNotice($"Could not delete session: {ex.Message}");
        }
    }

    // ── Commands: settings & app ────────────────────────────────────────────

    [RelayCommand]
    private void SaveSettings()
    {
        var name = PcDisplayName.Trim();
        if (name.Length == 0)
        {
            SettingsNote = "Choose a name for this PC.";
            return;
        }

        if (!int.TryParse(WsPortText.Trim(), out var port) || port is < 1024 or > 65535)
        {
            SettingsNote = "Port must be between 1024 and 65535.";
            return;
        }

        var previousPort = _host.Config.Current.WsPort;
        var previousInterval = _host.Config.Current.SampleIntervalMs;
        var interval = SelectedInterval?.Value ?? 500;
        _host.Config.Mutate(config =>
        {
            config.DisplayName = name;
            config.WsPort = port;
            config.SampleIntervalMs = interval;
            config.NotifyPhone = NotifyPhone;
            config.StartMinimized = StartMinimized;
            config.AutoStartAtLogin = AutoStartAtLogin;
        });

        try
        {
            SetAutoStart(AutoStartAtLogin);
        }
        catch (Exception ex)
        {
            SettingsNote = $"Saved, but sign-in launch could not be updated: {ex.Message}";
            ShowNotice(SettingsNote);
            return;
        }

        PcName = _host.Config.Current.PcName;
        SettingsNote = port != previousPort
            ? "Saved. Restart RigSight to apply the new Wi-Fi port."
            : interval != previousInterval
                ? "Saved. The sample interval applies to the next session."
                : "Saved. Display name changes apply after a restart.";
        ShowNotice("Settings saved.");
    }

    [RelayCommand]
    private void OpenDataFolder() => OpenFolder(RigSightPaths.Root);

    [RelayCommand]
    private void OpenLogsFolder() => OpenFolder(RigSightPaths.LogsDirectory);

    [RelayCommand]
    private void CopyTransportUrl()
    {
        try
        {
            Clipboard.SetText(TransportDetail);
            ShowNotice("Wi-Fi address copied.");
        }
        catch (Exception ex)
        {
            ShowNotice($"Could not copy: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearActivity() => Activity.Clear();

    [RelayCommand]
    private void Navigate(string? page)
    {
        if (Enum.TryParse<AppPage>(page, true, out var parsed))
            PageIndex = (int)parsed;
    }

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke();

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // Explorer is best effort.
        }
    }

    private static void SetAutoStart(bool enabled)
    {
        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
        if (key is null) return;
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return;
            key.SetValue("RigSight", $"\"{exe}\" --minimized");
        }
        else
        {
            key.DeleteValue("RigSight", throwOnMissingValue: false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clock.Stop();
        _noticeTimer.Stop();
        _host.Sessions.PromptRequested -= OnPromptRequested;
        _host.Sessions.PromptClosed -= OnPromptClosed;
        _host.Sessions.SessionStarted -= OnSessionStarted;
        _host.Sessions.SessionEnded -= OnSessionEnded;
        _host.Sessions.SampleProduced -= OnSampleProduced;
        _host.Sessions.StateChanged -= OnStateChanged;
        _host.Sessions.RecordingPausedChanged -= OnRecordingPausedChanged;
        _host.Sessions.Notice -= OnServiceNotice;
        _host.Hub.StatusChanged -= OnTransportChanged;
        Log.LineWritten -= OnLogLine;
    }
}
