using System.Text.Json.Serialization;

namespace RigSight.Core.Models;

public sealed class SessionInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("gameId")] public string GameId { get; set; } = "";
    [JsonPropertyName("gameName")] public string GameName { get; set; } = "";
    [JsonPropertyName("exeName")] public string ExeName { get; set; } = "";
    [JsonPropertyName("startedAt")] public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("sampleIntervalMs")] public int SampleIntervalMs { get; set; } = 500;
}

public sealed class SessionSummary
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("gameId")] public string GameId { get; set; } = "";
    [JsonPropertyName("gameName")] public string GameName { get; set; } = "";
    [JsonPropertyName("exeName")] public string ExeName { get; set; } = "";
    [JsonPropertyName("startedAt")] public DateTimeOffset StartedAt { get; set; }
    [JsonPropertyName("endedAt")] public DateTimeOffset EndedAt { get; set; }
    [JsonPropertyName("durationMs")] public long DurationMs { get; set; }
    [JsonPropertyName("sampleCount")] public int SampleCount { get; set; }

    [JsonPropertyName("avgFps")] public double? AvgFps { get; set; }
    [JsonPropertyName("medianFps")] public double? MedianFps { get; set; }
    [JsonPropertyName("low1Fps")] public double? Low1Fps { get; set; }
    [JsonPropertyName("low01Fps")] public double? Low01Fps { get; set; }
    [JsonPropertyName("avgFrameTimeMs")] public double? AvgFrameTimeMs { get; set; }

    [JsonPropertyName("maxCpuTempC")] public double? MaxCpuTempC { get; set; }
    [JsonPropertyName("maxGpuTempC")] public double? MaxGpuTempC { get; set; }
    [JsonPropertyName("maxCpuPowerW")] public double? MaxCpuPowerW { get; set; }
    [JsonPropertyName("maxGpuPowerW")] public double? MaxGpuPowerW { get; set; }
    [JsonPropertyName("avgCpuLoadPct")] public double? AvgCpuLoadPct { get; set; }
    [JsonPropertyName("avgGpuLoadPct")] public double? AvgGpuLoadPct { get; set; }
    [JsonPropertyName("maxVramUsedMb")] public double? MaxVramUsedMb { get; set; }

    [JsonPropertyName("stopReason")] public string StopReason { get; set; } = "user";
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;

    [JsonIgnore]
    public string DurationLabel
    {
        get
        {
            var ts = TimeSpan.FromMilliseconds(DurationMs);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m"
                : $"{ts.Minutes}m {ts.Seconds:D2}s";
        }
    }

    [JsonIgnore]
    public string DateLabel => StartedAt.ToLocalTime().ToString("MMM d, HH:mm");
}

public static class StopReasons
{
    public const string User = "user";
    public const string Phone = "phone";
    public const string GameExit = "game_exit";
    public const string AgentShutdown = "agent_shutdown";
    public const string Interrupted = "interrupted";
    public const string Demo = "demo";
}

public sealed class PromptInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("gameId")] public string GameId { get; set; } = "";
    [JsonPropertyName("gameName")] public string GameName { get; set; } = "";
    [JsonPropertyName("exeName")] public string ExeName { get; set; } = "";
    [JsonPropertyName("processId")] public int ProcessId { get; set; }
    [JsonPropertyName("expiresAt")] public long ExpiresAt { get; set; }
}

public sealed class PairedDevice
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("pairedAt")] public DateTimeOffset PairedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("lastSeenAt")] public DateTimeOffset? LastSeenAt { get; set; }
}
