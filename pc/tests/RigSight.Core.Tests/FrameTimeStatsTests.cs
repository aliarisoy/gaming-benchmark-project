using RigSight.Core.Telemetry;

namespace RigSight.Core.Tests;

public sealed class FrameTimeStatsTests
{
    [Fact]
    public void Snapshot_ReportsLiveAndSessionStats()
    {
        var stats = new FrameTimeStats();
        for (var i = 0; i < 100; i++) stats.AddFrame(10, i * 10);

        var snapshot = stats.Snapshot(1000);

        Assert.Equal(100, snapshot.Fps!.Value, 3);
        Assert.Equal(10, snapshot.FrameTimeMs!.Value, 3);
        Assert.Equal(100, snapshot.SessionFrameCount);
        Assert.InRange(stats.SessionAverageFps!.Value, 99.9, 100.1);
        Assert.InRange(stats.SessionMedianFps!.Value, 95, 105);
        Assert.InRange(stats.SessionLowFps(1)!.Value, 95, 105);
    }

    [Fact]
    public void Snapshot_QuietCapture_ReportsZeroFps()
    {
        var stats = new FrameTimeStats();
        for (var i = 0; i < 10; i++) stats.AddFrame(16, i * 16);

        var snapshot = stats.Snapshot(3000);

        Assert.Equal(0, snapshot.Fps);
        Assert.Null(snapshot.FrameTimeMs);
        Assert.Equal(10, snapshot.SessionFrameCount);
    }

    [Fact]
    public void Snapshot_UsesOnlyLastSecondForLiveFps()
    {
        var stats = new FrameTimeStats();
        for (var i = 0; i < 100; i++) stats.AddFrame(10, i * 10);
        for (var i = 0; i < 20; i++) stats.AddFrame(20, 10_000 + i * 20);

        var snapshot = stats.Snapshot(10_500);

        Assert.Equal(20, snapshot.Fps!.Value, 3);
        Assert.Equal(20, snapshot.FrameTimeMs!.Value, 3);
        Assert.Equal(120, snapshot.SessionFrameCount);
    }

    [Fact]
    public void InvalidFrameTimes_AreIgnored()
    {
        var stats = new FrameTimeStats();
        stats.AddFrame(0, 0);
        stats.AddFrame(-1, 10);
        stats.AddFrame(double.NaN, 20);
        stats.AddFrame(double.PositiveInfinity, 30);
        stats.AddFrame(60_001, 40);

        var snapshot = stats.Snapshot(100);
        Assert.Null(snapshot.Fps);
        Assert.Null(snapshot.FrameTimeMs);
        Assert.Equal(0, snapshot.SessionFrameCount);
    }

    [Fact]
    public void Reset_ClearsSession()
    {
        var stats = new FrameTimeStats();
        for (var i = 0; i < 10; i++) stats.AddFrame(16, i * 16);

        stats.Reset();

        Assert.Equal(0, stats.SessionFrameCount);
        Assert.Null(stats.SessionAverageFps);
        Assert.Null(stats.Snapshot(500).Fps);
    }
}
