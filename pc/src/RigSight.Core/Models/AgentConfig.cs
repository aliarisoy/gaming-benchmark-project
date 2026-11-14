using System.Text.Json.Serialization;
using RigSight.Core.Models;

namespace RigSight.Core.Models;

public sealed class AgentConfig
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("pcId")] public string PcId { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("wsPort")] public int WsPort { get; set; } = 47823;
    [JsonPropertyName("sampleIntervalMs")] public int SampleIntervalMs { get; set; } = 500;
    [JsonPropertyName("notifyPhone")] public bool NotifyPhone { get; set; } = true;
    [JsonPropertyName("startMinimized")] public bool StartMinimized { get; set; }
    [JsonPropertyName("autoStartAtLogin")] public bool AutoStartAtLogin { get; set; }
    [JsonPropertyName("games")] public List<GameProfile> Games { get; set; } = [];
    [JsonPropertyName("pairedDevices")] public List<PairedDevice> PairedDevices { get; set; } = [];

    [JsonIgnore]
    public string PcName => string.IsNullOrWhiteSpace(DisplayName) ? Environment.MachineName : DisplayName!;
}

public static class BuildInfo
{
    public const string Version = "0.1.0";
    public const int ProtocolVersion = 1;
}
