using System.Text.Json;
using RigSight.Core.Logging;
using RigSight.Core.Models;

namespace RigSight.Core.Storage;

public sealed class ConfigStore
{
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public AgentConfig Current { get; private set; }

    public ConfigStore()
    {
        Current = Load();
    }

    private static AgentConfig Load()
    {
        try
        {
            if (!File.Exists(RigSightPaths.ConfigFile)) return new AgentConfig();
            var json = File.ReadAllText(RigSightPaths.ConfigFile);
            var cfg = JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions) ?? new AgentConfig();
            if (string.IsNullOrWhiteSpace(cfg.PcId)) cfg.PcId = Guid.NewGuid().ToString("N");
            cfg.Games ??= [];
            cfg.PairedDevices ??= [];
            return cfg;
        }
        catch (Exception ex)
        {
            Log.Error("Could not read config; starting with defaults.", ex);
            try
            {
                var broken = RigSightPaths.ConfigFile + ".broken";
                if (File.Exists(RigSightPaths.ConfigFile)) File.Copy(RigSightPaths.ConfigFile, broken, true);
            }
            catch { /* ignore */ }
            return new AgentConfig();
        }
    }

    public void Mutate(Action<AgentConfig> change)
    {
        lock (_gate)
        {
            change(Current);
            Save();
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            try
            {
                var json = JsonSerializer.Serialize(Current, JsonOptions);
                var tmp = RigSightPaths.ConfigFile + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, RigSightPaths.ConfigFile, true);
            }
            catch (Exception ex)
            {
                Log.Error("Could not save config.", ex);
            }
        }
    }
}
