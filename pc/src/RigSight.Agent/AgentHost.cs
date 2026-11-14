using System.Diagnostics;
using System.IO;
using RigSight.Core.Logging;
using RigSight.Core.Models;
using RigSight.Core.Security;
using RigSight.Core.Storage;
using RigSight.Windows.Games;
using RigSight.Windows.Sensors;
using RigSight.Windows.Sessions;
using RigSight.Windows.Transport;

namespace RigSight.Agent;

/// <summary>Wires all PC-side services together for the lifetime of the app.</summary>
public sealed class AgentHost : IAsyncDisposable
{
    public AgentHost(AgentOptions options)
    {
        Options = options;
        Config = new ConfigStore();
        if (options.Port is { } port && port is > 1024 and < 65535)
            Config.Mutate(c => c.WsPort = port);

        Store = new SessionStore();
        Store.LogStats();
        Sensors = new HardwareSensorService();
        PairingSession = new PairingSession();
        Pairing = new PairingCoordinator(Config, PairingSession);
        Hub = new TransportHub(Config, Store, Pairing, enableBle: !options.NoBle);
        Watcher = new GameWatcher(Config);
        Sessions = new SessionService(Config, Store, Watcher, Sensors, Hub);
        Hub.SetCommandHandler(Sessions.HandleCommandAsync);
        Hub.SetSnapshotProvider(Sessions.GetSnapshot);
        Hub.StatusChanged += () => TransportChanged?.Invoke();
        Log.Info($"RigSight agent {BuildInfo.Version} starting on {Config.Current.PcName}.");
    }

    public AgentOptions Options { get; }
    public ConfigStore Config { get; }
    public SessionStore Store { get; }
    public HardwareSensorService Sensors { get; }
    public GameWatcher Watcher { get; }
    public PairingSession PairingSession { get; }
    public PairingCoordinator Pairing { get; }
    public TransportHub Hub { get; }
    public SessionService Sessions { get; }
    public string DataDirectory => RigSightPaths.Root;
    public TransportStatus TransportStatus => Hub.Status;
    public bool PresentMonAvailable => PresentMonService.IsAvailable;

    public event Action? TransportChanged;

    public Task StartAsync() => Hub.StartAsync();

    public int AddGames(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath)) continue;
                if (string.Equals(Path.GetFileName(fullPath), "RigSight.Agent.exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (Config.Current.Games.Any(g =>
                        string.Equals(g.ExePath, fullPath, StringComparison.OrdinalIgnoreCase))) continue;

                var info = FileVersionInfo.GetVersionInfo(fullPath);
                var name = !string.IsNullOrWhiteSpace(info.FileDescription)
                    ? info.FileDescription!.Trim()
                    : !string.IsNullOrWhiteSpace(info.ProductName)
                        ? info.ProductName!.Trim()
                        : Path.GetFileNameWithoutExtension(fullPath);

                Config.Current.Games.Add(new GameProfile
                {
                    Name = name,
                    ExeName = Path.GetFileName(fullPath),
                    ExePath = fullPath,
                });
                added++;
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not add game {path}: {ex.Message}");
            }
        }

        if (added > 0) Config.Save();
        Log.Info($"Added {added} game(s) to the library.");
        return added;
    }

    public void RemoveGame(string gameId)
    {
        Config.Mutate(c => c.Games.RemoveAll(g => g.Id == gameId));
        Log.Info($"Removed game {gameId} from the library.");
    }

    public void RevokeDevice(string deviceId)
    {
        Pairing.Revoke(deviceId);
        Log.Info($"Revoked paired device {deviceId}.");
    }

    public void RevokeAllDevices()
    {
        Pairing.RevokeAll();
        Log.Info("Revoked all paired phones.");
    }

    public IReadOnlyList<PairedDevice> PairedDevices => Config.Current.PairedDevices.ToArray();

    public async ValueTask DisposeAsync()
    {
        try { await Sessions.DisposeAsync(); } catch (Exception ex) { Log.Warn($"Session shutdown: {ex.Message}"); }
        try { await Hub.DisposeAsync(); } catch (Exception ex) { Log.Warn($"Hub shutdown: {ex.Message}"); }
        Watcher.Dispose();
        Sensors.Dispose();
        Log.Info("RigSight agent stopped.");
    }
}
