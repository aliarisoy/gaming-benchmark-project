using RigSight.Core.Models;

namespace RigSight.Core.Telemetry;

/// <summary>Deterministic synthetic telemetry for UI development and demos.</summary>
public sealed class DemoSampleGenerator
{
    private readonly Random _random = new(1337);
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private double _phase;

    public TelemetrySample Next(long elapsedMs)
    {
        _phase = elapsedMs / 1000d * 0.35;
        var load = 0.9 + Math.Sin(_phase * 0.7) * 0.06 + _random.NextDouble() * 0.02;
        var fpsTarget = 138 + Math.Sin(_phase) * 12 + _random.NextDouble() * 4;
        var gpuLoad = Math.Clamp(94 + Math.Sin(_phase * 1.7) * 5 + _random.NextDouble() * 2, 0, 100);
        var cpuLoad = Math.Clamp(38 + Math.Sin(_phase * 2.1) * 12 + _random.NextDouble() * 6, 0, 100);
        var gpuTemp = 65 + Math.Sin(_phase * 0.5) * 5 + _random.NextDouble() * 2;
        var cpuTemp = 61 + Math.Sin(_phase * 0.6 + 1) * 7 + _random.NextDouble() * 2;

        return new TelemetrySample
        {
            T = elapsedMs,
            Fps = Math.Round(fpsTarget, 1),
            FrameTimeMs = Math.Round(1000d / fpsTarget, 2),
            Low1Fps = Math.Round(fpsTarget * 0.79, 1),
            Low01Fps = Math.Round(fpsTarget * 0.66, 1),
            CpuLoadPct = Math.Round(cpuLoad, 1),
            CpuTempC = Math.Round(cpuTemp, 1),
            CpuPowerW = Math.Round(65 + cpuLoad * 1.1, 1),
            CpuClockMhz = Math.Round(4900 + Math.Sin(_phase * 2.1) * 240, 0),
            GpuLoadPct = Math.Round(gpuLoad, 1),
            GpuTempC = Math.Round(gpuTemp, 1),
            GpuHotspotC = Math.Round(gpuTemp + 11, 1),
            GpuPowerW = Math.Round(280 + gpuLoad * 0.55, 1),
            GpuClockMhz = Math.Round(1890 + Math.Sin(_phase * 0.4) * 45, 0),
            VramUsedMb = Math.Round(7600 + Math.Sin(_phase * 0.25) * 420, 0),
            VramTotalMb = 10240,
            RamUsedMb = Math.Round(17600 + Math.Sin(_phase * 0.2) * 900, 0),
            RamTotalMb = 32768,
        };
    }
}
