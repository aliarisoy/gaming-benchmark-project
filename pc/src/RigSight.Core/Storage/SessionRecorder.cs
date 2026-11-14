using System.Globalization;
using System.Text;
using System.Text.Json;
using RigSight.Core.Logging;
using RigSight.Core.Models;
using RigSight.Core.Protocol;
using RigSight.Core.Util;

namespace RigSight.Core.Storage;

/// <summary>Writes raw samples as JSONL and a final summary next to them.</summary>
public sealed class SessionRecorder : IDisposable
{
    private readonly object _gate = new();
    private readonly List<TelemetrySample> _samples = [];
    private readonly string _directory;
    private StreamWriter? _writer;
    private bool _completed;

    public SessionRecorder(GameProfile game, int sampleIntervalMs, string? sessionsDirectory = null)
    {
        Game = game;
        var now = DateTimeOffset.UtcNow;
        var id = $"{now:yyyyMMdd'T'HHmmss'Z'}-{Guid.NewGuid().ToString("N")[..6]}";
        Info = new SessionInfo
        {
            Id = id,
            GameId = game.Id,
            GameName = game.Name,
            ExeName = game.ExeName,
            StartedAt = now,
            SampleIntervalMs = sampleIntervalMs,
        };
        _directory = Path.Combine(sessionsDirectory ?? RigSightPaths.SessionsDirectory, id);
        Directory.CreateDirectory(_directory);
        Start();
    }

    public GameProfile Game { get; }
    public SessionInfo Info { get; }
    public string DirectoryPath => _directory;
    public int SampleCount { get { lock (_gate) return _samples.Count; } }
    public IReadOnlyList<TelemetrySample> Samples { get { lock (_gate) return _samples.ToArray(); } }

    private void Start()
    {
        var meta = new
        {
            session = Info,
            game = Game,
            createdBy = $"RigSight {BuildInfo.Version}",
            schemaVersion = 1,
        };
        File.WriteAllText(Path.Combine(_directory, "meta.json"),
            JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
        _writer = new StreamWriter(Path.Combine(_directory, "samples.jsonl"), append: false, Encoding.UTF8)
        {
            AutoFlush = true,
        };
        Log.Info($"Recording session {Info.Id} for {Game.Name}.");
    }

    public void Append(TelemetrySample sample)
    {
        lock (_gate)
        {
            if (_writer is null || _completed) return;
            _samples.Add(sample);
            try
            {
                _writer.WriteLine(JsonSerializer.Serialize(sample, ProtocolJson.Options));
            }
            catch (Exception ex)
            {
                Log.Error("Could not append telemetry sample.", ex);
            }
        }
    }

    public SessionSummary Complete(
        string stopReason,
        double? sessionLow1Fps = null,
        double? sessionLow01Fps = null,
        double? sessionAvgFps = null,
        double? sessionMedianFps = null)
    {
        lock (_gate)
        {
            if (_completed && Summary is not null) return Summary;

            var endedAt = DateTimeOffset.UtcNow;
            var fps = _samples.Select(s => s.Fps);
            var low1 = sessionLow1Fps ?? Stats.Percentile(
                _samples.Where(s => s.Low1Fps is > 0).Select(s => s.Low1Fps!.Value), 50);
            var low01 = sessionLow01Fps ?? Stats.Percentile(
                _samples.Where(s => s.Low01Fps is > 0).Select(s => s.Low01Fps!.Value), 50);

            Summary = new SessionSummary
            {
                Id = Info.Id,
                GameId = Info.GameId,
                GameName = Info.GameName,
                ExeName = Info.ExeName,
                StartedAt = Info.StartedAt,
                EndedAt = endedAt,
                DurationMs = Math.Max(0, (long)(endedAt - Info.StartedAt).TotalMilliseconds),
                SampleCount = _samples.Count,
                AvgFps = sessionAvgFps ?? Stats.Mean(fps),
                MedianFps = sessionMedianFps ?? Stats.Median(fps),
                Low1Fps = low1,
                Low01Fps = low01,
                AvgFrameTimeMs = Stats.Mean(_samples.Select(s => s.FrameTimeMs)),
                MaxCpuTempC = Stats.Max(_samples.Select(s => s.CpuTempC)),
                MaxGpuTempC = Stats.Max(_samples.Select(s => s.GpuTempC)),
                MaxCpuPowerW = Stats.Max(_samples.Select(s => s.CpuPowerW)),
                MaxGpuPowerW = Stats.Max(_samples.Select(s => s.GpuPowerW)),
                AvgCpuLoadPct = Stats.Mean(_samples.Select(s => s.CpuLoadPct)),
                AvgGpuLoadPct = Stats.Mean(_samples.Select(s => s.GpuLoadPct)),
                MaxVramUsedMb = Stats.Max(_samples.Select(s => s.VramUsedMb)),
                StopReason = stopReason,
            };

            try
            {
                File.WriteAllText(Path.Combine(_directory, "summary.json"),
                    JsonSerializer.Serialize(Summary, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Log.Error("Could not write session summary.", ex);
            }

            _completed = true;
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
            Log.Info($"Session {Info.Id} finished ({stopReason}); {Summary.SampleCount} samples, {Summary.DurationLabel}.");
            return Summary;
        }
    }

    public SessionSummary? Summary { get; private set; }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_completed) return;
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    public static IReadOnlyList<TelemetrySample> ReadSamples(string sessionId, int maxSamples = 0)
    {
        var path = Path.Combine(SessionDirectory(sessionId), "samples.jsonl");
        if (!File.Exists(path)) return [];

        var list = new List<TelemetrySample>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var sample = JsonSerializer.Deserialize<TelemetrySample>(line, ProtocolJson.Options);
            if (sample is not null) list.Add(sample);
        }

        if (maxSamples <= 0 || list.Count <= maxSamples) return list;

        // Evenly downsample while always keeping the first and last sample.
        var result = new List<TelemetrySample>(maxSamples);
        var step = (list.Count - 1) / (double)(maxSamples - 1);
        for (var i = 0; i < maxSamples; i++)
            result.Add(list[(int)Math.Round(i * step)]);
        return result;
    }

    public static string ExportCsv(string sessionId, string? destination = null)
    {
        var samples = ReadSamples(sessionId);
        destination ??= Path.Combine(RigSightPaths.Root, "exports", $"{sessionId}.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        var sb = new StringBuilder();
        sb.AppendLine("t_ms,fps,frame_time_ms,low1_fps,low01_fps,cpu_load_pct,cpu_temp_c,cpu_power_w,cpu_clock_mhz,gpu_load_pct,gpu_temp_c,gpu_hotspot_c,gpu_power_w,gpu_clock_mhz,vram_used_mb,vram_total_mb,ram_used_mb,ram_total_mb");
        foreach (var s in samples)
        {
            sb.Append(s.T.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Fmt(s.Fps)).Append(',')
              .Append(Fmt(s.FrameTimeMs)).Append(',')
              .Append(Fmt(s.Low1Fps)).Append(',')
              .Append(Fmt(s.Low01Fps)).Append(',')
              .Append(Fmt(s.CpuLoadPct)).Append(',')
              .Append(Fmt(s.CpuTempC)).Append(',')
              .Append(Fmt(s.CpuPowerW)).Append(',')
              .Append(Fmt(s.CpuClockMhz)).Append(',')
              .Append(Fmt(s.GpuLoadPct)).Append(',')
              .Append(Fmt(s.GpuTempC)).Append(',')
              .Append(Fmt(s.GpuHotspotC)).Append(',')
              .Append(Fmt(s.GpuPowerW)).Append(',')
              .Append(Fmt(s.GpuClockMhz)).Append(',')
              .Append(Fmt(s.VramUsedMb)).Append(',')
              .Append(Fmt(s.VramTotalMb)).Append(',')
              .Append(Fmt(s.RamUsedMb)).Append(',')
              .Append(Fmt(s.RamTotalMb)).AppendLine();
        }

        File.WriteAllText(destination, sb.ToString(), Encoding.UTF8);
        return destination;
    }

    private static string Fmt(double? v) => v?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";

    private static string SessionDirectory(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) ||
            sessionId.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
            throw new ArgumentException("Invalid session id.", nameof(sessionId));
        return Path.Combine(RigSightPaths.SessionsDirectory, sessionId);
    }

    public static void DeleteFiles(string sessionId)
    {
        var dir = SessionDirectory(sessionId);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
