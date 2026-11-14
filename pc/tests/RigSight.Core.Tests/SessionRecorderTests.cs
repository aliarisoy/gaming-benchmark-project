using RigSight.Core.Models;
using RigSight.Core.Storage;

namespace RigSight.Core.Tests;

public sealed class SessionRecorderTests : IDisposable
{
    private readonly string _sessionsDirectory = Path.Combine(
        Path.GetTempPath(), "RigSightTests", "recorder-" + Guid.NewGuid().ToString("N"));

    private static GameProfile Game => new()
    {
        Id = "game-1",
        Name = "Test Game",
        ExeName = "test.exe",
    };

    [Fact]
    public void AppendAndComplete_WritesFilesAndSummary()
    {
        using var recorder = new SessionRecorder(Game, 500, _sessionsDirectory);
        recorder.Append(new TelemetrySample
        {
            T = 0, Fps = 100, FrameTimeMs = 10, Low1Fps = 80, Low01Fps = 70,
            CpuTempC = 70, GpuTempC = 75, CpuPowerW = 100, GpuPowerW = 300,
            CpuLoadPct = 40, GpuLoadPct = 90, VramUsedMb = 8_000,
        });
        recorder.Append(new TelemetrySample
        {
            T = 500, Fps = 120, FrameTimeMs = 8.333, Low1Fps = 90, Low01Fps = 80,
            CpuTempC = 75, GpuTempC = 80, CpuPowerW = 110, GpuPowerW = 320,
            CpuLoadPct = 50, GpuLoadPct = 95, VramUsedMb = 8_200,
        });

        var summary = recorder.Complete(StopReasons.User);

        Assert.Equal(2, summary.SampleCount);
        Assert.Equal(110, summary.AvgFps!.Value, 3);
        Assert.Equal(110, summary.MedianFps!.Value, 3);
        Assert.Equal(85, summary.Low1Fps!.Value, 3);
        Assert.Equal(75, summary.Low01Fps!.Value, 3);
        Assert.Equal(75, summary.MaxCpuTempC!.Value, 3);
        Assert.Equal(80, summary.MaxGpuTempC!.Value, 3);
        Assert.Equal(110, summary.MaxCpuPowerW!.Value, 3);
        Assert.Equal(320, summary.MaxGpuPowerW!.Value, 3);
        Assert.Equal(45, summary.AvgCpuLoadPct!.Value, 3);
        Assert.Equal(92.5, summary.AvgGpuLoadPct!.Value, 3);
        Assert.Equal(8_200, summary.MaxVramUsedMb!.Value, 3);
        Assert.Equal(StopReasons.User, summary.StopReason);
        Assert.True(summary.DurationMs >= 0);

        Assert.True(File.Exists(Path.Combine(recorder.DirectoryPath, "meta.json")));
        Assert.True(File.Exists(Path.Combine(recorder.DirectoryPath, "summary.json")));
        var sampleLines = File.ReadAllLines(Path.Combine(recorder.DirectoryPath, "samples.jsonl"))
            .Count(line => !string.IsNullOrWhiteSpace(line));
        Assert.Equal(2, sampleLines);
    }

    [Fact]
    public void Complete_IsIdempotent()
    {
        using var recorder = new SessionRecorder(Game, 500, _sessionsDirectory);
        recorder.Append(new TelemetrySample { T = 0, Fps = 60, FrameTimeMs = 16.67 });

        var first = recorder.Complete(StopReasons.User);
        var second = recorder.Complete(StopReasons.Phone);

        Assert.Same(first, second);
        Assert.Equal(StopReasons.User, second.StopReason);
        Assert.Equal(1, second.SampleCount);
    }

    [Fact]
    public void AppendAfterComplete_IsIgnored()
    {
        using var recorder = new SessionRecorder(Game, 500, _sessionsDirectory);
        recorder.Append(new TelemetrySample { T = 0, Fps = 60 });
        var summary = recorder.Complete(StopReasons.User);

        recorder.Append(new TelemetrySample { T = 500, Fps = 120 });

        Assert.Equal(1, recorder.SampleCount);
        Assert.Equal(1, summary.SampleCount);
    }

    [Fact]
    public void CompleteWithoutSamples_HasNullMetrics()
    {
        using var recorder = new SessionRecorder(Game, 500, _sessionsDirectory);

        var summary = recorder.Complete(StopReasons.AgentShutdown);

        Assert.Equal(0, summary.SampleCount);
        Assert.Null(summary.AvgFps);
        Assert.Null(summary.MedianFps);
        Assert.Null(summary.MaxCpuTempC);
        Assert.True(summary.DurationMs >= 0);
    }

    public void Dispose()
    {
        try { Directory.Delete(_sessionsDirectory, recursive: true); }
        catch { /* best-effort test cleanup */ }
    }
}
