using System.Diagnostics;
using System.Runtime.InteropServices;
using RigSight.Core.Logging;
using RigSight.Core.Models;
using RigSight.Core.Storage;

namespace RigSight.Windows.Games;

public sealed record GameLaunch(GameProfile Game, int ProcessId, string? ProcessPath);

public sealed record GameExit(GameProfile Game, int ProcessId);

/// <summary>
/// Polls the process list for games in the user's library. Only configured process
/// names are queried, so this stays cheap even with hundreds of system processes.
/// </summary>
public sealed class GameWatcher : IDisposable
{
    private readonly ConfigStore _config;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _running = new(); // gameId -> pid
    private readonly HashSet<int> _suppressed = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private bool _seeded;

    public GameWatcher(ConfigStore config)
    {
        _config = config;
        _thread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name = "RigSight game watcher",
        };
        _thread.Start();
    }

    public event Action<GameLaunch>? GameDetected;
    public event Action<GameExit>? GameExited;

    /// <summary>Game ids the watcher currently sees running; safe to poll from the UI.</summary>
    public IReadOnlyCollection<string> RunningGameIds
    {
        get { lock (_gate) return _running.Keys.ToArray(); }
    }

    public void SuppressProcess(int pid)
    {
        lock (_gate) _suppressed.Add(pid);
    }

    public bool IsProcessRunning(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch { return false; }
    }

    public (GameProfile Game, Process Process)? FindRunning(GameProfile game)
    {
        try
        {
            foreach (var process in FindProcesses(game))
            {
                var path = TryGetPath(process);
                if (game.Matches(process.ProcessName + ".exe", path))
                    return (game, process);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not find running game {game.Name}: {ex.Message}");
        }
        return null;
    }

    public (GameProfile Game, Process Process)? FindForeground()
    {
        var pid = ForegroundWindow.GetForegroundProcessId();
        if (pid == 0) return null;
        foreach (var game in _config.Current.Games)
        {
            foreach (var process in FindProcesses(game))
            {
                if (process.Id != pid) continue;
                var path = TryGetPath(process);
                if (game.Matches(process.ProcessName + ".exe", path)) return (game, process);
            }
        }
        return null;
    }

    public IEnumerable<GameProfile> RunningGames()
    {
        foreach (var game in _config.Current.Games.ToArray())
        {
            var found = FindRunning(game);
            if (found is not null)
            {
                found.Value.Process.Dispose();
                yield return game;
            }
        }
    }

    private void PollLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                Scan();
            }
            catch (Exception ex)
            {
                Log.Warn($"Game scan failed: {ex.Message}");
            }

            try { _cts.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(1500)); }
            catch { break; }
        }
    }

    private void Scan()
    {
        var games = _config.Current.Games.ToArray();
        var seen = new HashSet<int>();

        foreach (var game in games)
        {
            if (string.IsNullOrWhiteSpace(game.ExeName)) continue;
            var found = FindRunning(game);
            if (found is null) continue;
            var process = found.Value.Process;
            using (process)
            {
                var pid = process.Id;
                seen.Add(pid);

                bool already;
                lock (_gate)
                {
                    already = _running.TryGetValue(game.Id, out var knownPid) && knownPid == pid;
                    if (!already)
                    {
                        _running[game.Id] = pid;
                        if (_suppressed.Contains(pid)) already = true;
                    }
                }

                if (!already && _seeded)
                {
                    Log.Info($"Detected game launch: {game.Name} (pid {pid}).");
                    try { GameDetected?.Invoke(new GameLaunch(game, pid, TryGetPath(process))); }
                    catch (Exception ex) { Log.Error("GameDetected handler failed.", ex); }
                }
            }
        }

        List<GameExit> exits = [];
        lock (_gate)
        {
            foreach (var (gameId, pid) in _running.ToArray())
            {
                if (seen.Contains(pid)) continue;
                _running.Remove(gameId);
                _suppressed.Remove(pid);
                var game = games.FirstOrDefault(g => g.Id == gameId);
                if (game is not null) exits.Add(new GameExit(game, pid));
            }
        }

        foreach (var exit in exits)
        {
            Log.Info($"Game exited: {exit.Game.Name} (pid {exit.ProcessId}).");
            try { GameExited?.Invoke(exit); }
            catch (Exception ex) { Log.Error("GameExited handler failed.", ex); }
        }

        if (!_seeded)
        {
            // Don't prompt for games that were already running when RigSight started.
            // The user can start those explicitly from the hotkey or tray.
            _seeded = true;
        }
    }

    private static IEnumerable<Process> FindProcesses(GameProfile game)
    {
        var name = Path.GetFileNameWithoutExtension(game.ExeName);
        if (string.IsNullOrWhiteSpace(name)) return [];
        try { return Process.GetProcessesByName(name); }
        catch { return []; }
    }

    private static string? TryGetPath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _thread.Join(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _cts.Dispose();
    }
}

internal static class ForegroundWindow
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static int GetForegroundProcessId()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(hwnd, out var pid);
            return (int)pid;
        }
        catch { return 0; }
    }
}
