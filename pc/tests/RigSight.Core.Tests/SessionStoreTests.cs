using Microsoft.Data.Sqlite;
using RigSight.Core.Models;
using RigSight.Core.Storage;

namespace RigSight.Core.Tests;

public sealed class SessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "RigSightTests", "db-" + Guid.NewGuid().ToString("N"));

    private SessionStore CreateStore()
    {
        Directory.CreateDirectory(_directory);
        return new SessionStore(Path.Combine(_directory, "sessions.db"));
    }

    [Fact]
    public void SaveThenGet_RoundTripsAllFields()
    {
        var store = CreateStore();
        var summary = MakeSummary();

        store.Save(summary);
        var loaded = store.Get(summary.Id);

        Assert.NotNull(loaded);
        Assert.Equal(summary.Id, loaded!.Id);
        Assert.Equal(summary.GameId, loaded.GameId);
        Assert.Equal(summary.GameName, loaded.GameName);
        Assert.Equal(summary.ExeName, loaded.ExeName);
        Assert.Equal(summary.StartedAt, loaded.StartedAt);
        Assert.Equal(summary.EndedAt, loaded.EndedAt);
        Assert.Equal(summary.DurationMs, loaded.DurationMs);
        Assert.Equal(summary.SampleCount, loaded.SampleCount);
        Assert.Equal(summary.AvgFps, loaded.AvgFps);
        Assert.Equal(summary.MedianFps, loaded.MedianFps);
        Assert.Equal(summary.Low1Fps, loaded.Low1Fps);
        Assert.Equal(summary.Low01Fps, loaded.Low01Fps);
        Assert.Equal(summary.AvgFrameTimeMs, loaded.AvgFrameTimeMs);
        Assert.Equal(summary.MaxCpuTempC, loaded.MaxCpuTempC);
        Assert.Equal(summary.MaxGpuTempC, loaded.MaxGpuTempC);
        Assert.Equal(summary.MaxCpuPowerW, loaded.MaxCpuPowerW);
        Assert.Equal(summary.MaxGpuPowerW, loaded.MaxGpuPowerW);
        Assert.Equal(summary.AvgCpuLoadPct, loaded.AvgCpuLoadPct);
        Assert.Equal(summary.AvgGpuLoadPct, loaded.AvgGpuLoadPct);
        Assert.Equal(summary.MaxVramUsedMb, loaded.MaxVramUsedMb);
        Assert.Equal(summary.StopReason, loaded.StopReason);
        Assert.Equal(summary.SchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public void Save_NullMetrics_RoundTripAsNull()
    {
        var store = CreateStore();
        var summary = MakeSummary();
        summary.AvgFps = null;
        summary.MaxGpuTempC = null;
        summary.MaxVramUsedMb = null;

        store.Save(summary);
        var loaded = store.Get(summary.Id);

        Assert.NotNull(loaded);
        Assert.Null(loaded!.AvgFps);
        Assert.Null(loaded.MaxGpuTempC);
        Assert.Null(loaded.MaxVramUsedMb);
    }

    [Fact]
    public void GetAll_OrdersByStartTimeDescending()
    {
        var store = CreateStore();
        var older = MakeSummary();
        older.Id = "older";
        older.StartedAt = DateTimeOffset.UtcNow.AddHours(-3);
        var newer = MakeSummary();
        newer.Id = "newer";
        newer.StartedAt = DateTimeOffset.UtcNow.AddHours(-1);

        store.Save(older);
        store.Save(newer);

        var all = store.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("newer", all[0].Id);
        Assert.Equal("older", all[1].Id);
    }

    [Fact]
    public void Save_ExistingId_UpdatesInsteadOfDuplicating()
    {
        var store = CreateStore();
        var summary = MakeSummary();
        store.Save(summary);

        summary.SampleCount = 999;
        summary.AvgFps = 60;
        summary.StopReason = StopReasons.Phone;
        store.Save(summary);

        var all = store.GetAll();
        Assert.Single(all);
        Assert.Equal(999, all[0].SampleCount);
        Assert.Equal(60, all[0].AvgFps);
        Assert.Equal(StopReasons.Phone, all[0].StopReason);
    }

    [Fact]
    public void Delete_RemovesSession()
    {
        var store = CreateStore();
        var summary = MakeSummary();
        store.Save(summary);

        store.Delete(summary.Id);

        Assert.Null(store.Get(summary.Id));
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void GetUnknownId_ReturnsNull()
    {
        var store = CreateStore();
        Assert.Null(store.Get("missing"));
    }

    private static SessionSummary MakeSummary() => new()
    {
        Id = "session-" + Guid.NewGuid().ToString("N")[..8],
        GameId = "game-1",
        GameName = "Test Game",
        ExeName = "test.exe",
        StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
        EndedAt = DateTimeOffset.UtcNow,
        DurationMs = 1_800_000,
        SampleCount = 3600,
        AvgFps = 142.5,
        MedianFps = 144,
        Low1Fps = 110,
        Low01Fps = 95.5,
        AvgFrameTimeMs = 7.02,
        MaxCpuTempC = 74,
        MaxGpuTempC = 81,
        MaxCpuPowerW = 128,
        MaxGpuPowerW = 340,
        AvgCpuLoadPct = 42.5,
        AvgGpuLoadPct = 96.2,
        MaxVramUsedMb = 9_500,
        StopReason = StopReasons.User,
        SchemaVersion = 1,
    };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* best-effort test cleanup */ }
    }
}
