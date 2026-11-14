using System.Text.Json.Serialization;

namespace RigSight.Core.Models;

/// <summary>
/// One dashboard/recording sample. All properties are nullable so unsupported
/// sensors are omitted from the wire and show as "—" on the phone.
/// </summary>
public sealed class TelemetrySample
{
    /// <summary>Milliseconds since the session started.</summary>
    [JsonPropertyName("t")] public long T { get; set; }

    [JsonPropertyName("fps")] public double? Fps { get; set; }
    [JsonPropertyName("frameTimeMs")] public double? FrameTimeMs { get; set; }
    [JsonPropertyName("low1Fps")] public double? Low1Fps { get; set; }
    [JsonPropertyName("low01Fps")] public double? Low01Fps { get; set; }

    [JsonPropertyName("cpuLoadPct")] public double? CpuLoadPct { get; set; }
    [JsonPropertyName("cpuTempC")] public double? CpuTempC { get; set; }
    [JsonPropertyName("cpuPowerW")] public double? CpuPowerW { get; set; }
    [JsonPropertyName("cpuClockMhz")] public double? CpuClockMhz { get; set; }

    [JsonPropertyName("gpuLoadPct")] public double? GpuLoadPct { get; set; }
    [JsonPropertyName("gpuTempC")] public double? GpuTempC { get; set; }
    [JsonPropertyName("gpuHotspotC")] public double? GpuHotspotC { get; set; }
    [JsonPropertyName("gpuPowerW")] public double? GpuPowerW { get; set; }
    [JsonPropertyName("gpuClockMhz")] public double? GpuClockMhz { get; set; }

    [JsonPropertyName("vramUsedMb")] public double? VramUsedMb { get; set; }
    [JsonPropertyName("vramTotalMb")] public double? VramTotalMb { get; set; }
    [JsonPropertyName("ramUsedMb")] public double? RamUsedMb { get; set; }
    [JsonPropertyName("ramTotalMb")] public double? RamTotalMb { get; set; }

    public TelemetrySample Clone() => (TelemetrySample)MemberwiseClone();
}
