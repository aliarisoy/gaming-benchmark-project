using Microsoft.Data.Sqlite;
using RigSight.Core.Logging;
using RigSight.Core.Models;

namespace RigSight.Core.Storage;

/// <summary>SQLite index of finished sessions. Raw samples live beside it as JSONL.</summary>
public sealed class SessionStore
{
    private readonly string _connectionString;
    private readonly object _gate = new();

    public SessionStore(string? databasePath = null)
    {
        var path = databasePath ?? RigSightPaths.DatabaseFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        Initialize();
    }

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS sessions (
                    id TEXT PRIMARY KEY,
                    game_id TEXT NOT NULL,
                    game_name TEXT NOT NULL,
                    exe_name TEXT NOT NULL,
                    started_at TEXT NOT NULL,
                    ended_at TEXT NOT NULL,
                    duration_ms INTEGER NOT NULL,
                    sample_count INTEGER NOT NULL,
                    avg_fps REAL NULL,
                    median_fps REAL NULL,
                    low1_fps REAL NULL,
                    low01_fps REAL NULL,
                    avg_frame_time_ms REAL NULL,
                    max_cpu_temp_c REAL NULL,
                    max_gpu_temp_c REAL NULL,
                    max_cpu_power_w REAL NULL,
                    max_gpu_power_w REAL NULL,
                    avg_cpu_load_pct REAL NULL,
                    avg_gpu_load_pct REAL NULL,
                    max_vram_used_mb REAL NULL,
                    stop_reason TEXT NOT NULL,
                    schema_version INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_sessions_started ON sessions(started_at DESC);
                """;
            cmd.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public void Save(SessionSummary s)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sessions (
                    id, game_id, game_name, exe_name, started_at, ended_at, duration_ms, sample_count,
                    avg_fps, median_fps, low1_fps, low01_fps, avg_frame_time_ms,
                    max_cpu_temp_c, max_gpu_temp_c, max_cpu_power_w, max_gpu_power_w,
                    avg_cpu_load_pct, avg_gpu_load_pct, max_vram_used_mb, stop_reason, schema_version)
                VALUES (
                    $id, $gameId, $gameName, $exeName, $startedAt, $endedAt, $durationMs, $sampleCount,
                    $avgFps, $medianFps, $low1Fps, $low01Fps, $avgFrameTimeMs,
                    $maxCpuTemp, $maxGpuTemp, $maxCpuPower, $maxGpuPower,
                    $avgCpuLoad, $avgGpuLoad, $maxVram, $stopReason, $schemaVersion)
                ON CONFLICT(id) DO UPDATE SET
                    ended_at = excluded.ended_at, duration_ms = excluded.duration_ms,
                    sample_count = excluded.sample_count, avg_fps = excluded.avg_fps,
                    median_fps = excluded.median_fps, low1_fps = excluded.low1_fps,
                    low01_fps = excluded.low01_fps, avg_frame_time_ms = excluded.avg_frame_time_ms,
                    max_cpu_temp_c = excluded.max_cpu_temp_c, max_gpu_temp_c = excluded.max_gpu_temp_c,
                    max_cpu_power_w = excluded.max_cpu_power_w, max_gpu_power_w = excluded.max_gpu_power_w,
                    avg_cpu_load_pct = excluded.avg_cpu_load_pct, avg_gpu_load_pct = excluded.avg_gpu_load_pct,
                    max_vram_used_mb = excluded.max_vram_used_mb, stop_reason = excluded.stop_reason;
                """;
            cmd.Parameters.AddWithValue("$id", s.Id);
            cmd.Parameters.AddWithValue("$gameId", s.GameId);
            cmd.Parameters.AddWithValue("$gameName", s.GameName);
            cmd.Parameters.AddWithValue("$exeName", s.ExeName);
            cmd.Parameters.AddWithValue("$startedAt", s.StartedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$endedAt", s.EndedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$durationMs", s.DurationMs);
            cmd.Parameters.AddWithValue("$sampleCount", s.SampleCount);
            AddNullable(cmd, "$avgFps", s.AvgFps);
            AddNullable(cmd, "$medianFps", s.MedianFps);
            AddNullable(cmd, "$low1Fps", s.Low1Fps);
            AddNullable(cmd, "$low01Fps", s.Low01Fps);
            AddNullable(cmd, "$avgFrameTimeMs", s.AvgFrameTimeMs);
            AddNullable(cmd, "$maxCpuTemp", s.MaxCpuTempC);
            AddNullable(cmd, "$maxGpuTemp", s.MaxGpuTempC);
            AddNullable(cmd, "$maxCpuPower", s.MaxCpuPowerW);
            AddNullable(cmd, "$maxGpuPower", s.MaxGpuPowerW);
            AddNullable(cmd, "$avgCpuLoad", s.AvgCpuLoadPct);
            AddNullable(cmd, "$avgGpuLoad", s.AvgGpuLoadPct);
            AddNullable(cmd, "$maxVram", s.MaxVramUsedMb);
            cmd.Parameters.AddWithValue("$stopReason", s.StopReason);
            cmd.Parameters.AddWithValue("$schemaVersion", s.SchemaVersion);
            cmd.ExecuteNonQuery();
        }
    }

    public List<SessionSummary> GetAll(int limit = 200)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM sessions ORDER BY started_at DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", limit);
            using var reader = cmd.ExecuteReader();
            var list = new List<SessionSummary>();
            while (reader.Read()) list.Add(Read(reader));
            return list;
        }
    }

    public SessionSummary? Get(string id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM sessions WHERE id = $id LIMIT 1";
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM sessions WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    private static void AddNullable(SqliteCommand cmd, string name, double? value)
        => cmd.Parameters.AddWithValue(name, value is null ? DBNull.Value : value.Value);

    private static double? Dbl(SqliteDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetDouble(i);
    }

    private static SessionSummary Read(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        GameId = r.GetString(r.GetOrdinal("game_id")),
        GameName = r.GetString(r.GetOrdinal("game_name")),
        ExeName = r.GetString(r.GetOrdinal("exe_name")),
        StartedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("started_at"))),
        EndedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("ended_at"))),
        DurationMs = r.GetInt64(r.GetOrdinal("duration_ms")),
        SampleCount = r.GetInt32(r.GetOrdinal("sample_count")),
        AvgFps = Dbl(r, "avg_fps"),
        MedianFps = Dbl(r, "median_fps"),
        Low1Fps = Dbl(r, "low1_fps"),
        Low01Fps = Dbl(r, "low01_fps"),
        AvgFrameTimeMs = Dbl(r, "avg_frame_time_ms"),
        MaxCpuTempC = Dbl(r, "max_cpu_temp_c"),
        MaxGpuTempC = Dbl(r, "max_gpu_temp_c"),
        MaxCpuPowerW = Dbl(r, "max_cpu_power_w"),
        MaxGpuPowerW = Dbl(r, "max_gpu_power_w"),
        AvgCpuLoadPct = Dbl(r, "avg_cpu_load_pct"),
        AvgGpuLoadPct = Dbl(r, "avg_gpu_load_pct"),
        MaxVramUsedMb = Dbl(r, "max_vram_used_mb"),
        StopReason = r.GetString(r.GetOrdinal("stop_reason")),
        SchemaVersion = r.GetInt32(r.GetOrdinal("schema_version")),
    };

    public void LogStats()
    {
        try
        {
            Log.Info($"Session index ready ({GetAll().Count} sessions).");
        }
        catch (Exception ex)
        {
            Log.Error("Session index check failed.", ex);
        }
    }
}
