namespace RigSight.Core.Telemetry;

public readonly record struct FrameSnapshot(
    double? Fps,
    double? FrameTimeMs,
    double? Low1Fps,
    double? Low01Fps,
    long SessionFrameCount);

/// <summary>
/// Aggregates PresentMon frame times into glanceable stats. Keeps a 60 s rolling
/// window for live 1%/0.1% lows and a fixed histogram for whole-session percentiles,
/// so a multi-hour session does not grow without bound.
/// </summary>
public sealed class FrameTimeStats
{
    private const double BucketWidthMs = 0.5;
    private const int BucketCount = 2000; // 0.5 ms .. 1000 ms, last bucket is overflow
    private const long RollingWindowMs = 60_000;

    private readonly object _gate = new();
    private readonly LinkedList<Frame> _rolling = new();
    private readonly LinkedList<Frame> _recent = new();
    private readonly long[] _histogram = new long[BucketCount + 1];
    private long _sessionFrames;
    private double _sessionFrameTimeSum;
    private long _lastFrameAtMs;
    private bool _hasFrames;

    private readonly record struct Frame(long AtMs, double FrameTimeMs);

    public void AddFrame(double frameTimeMs, long atMs)
    {
        if (double.IsNaN(frameTimeMs) || double.IsInfinity(frameTimeMs) || frameTimeMs <= 0)
            return;
        if (frameTimeMs > 60_000) return;

        lock (_gate)
        {
            var frame = new Frame(atMs, frameTimeMs);
            _rolling.AddLast(frame);
            _recent.AddLast(frame);
            _lastFrameAtMs = atMs;
            _hasFrames = true;
            _sessionFrames++;
            _sessionFrameTimeSum += frameTimeMs;

            var bucket = Math.Min(BucketCount, (int)(frameTimeMs / BucketWidthMs));
            _histogram[bucket]++;

            TrimOlderThan(atMs - RollingWindowMs);
        }
    }

    private void TrimOlderThan(long cutoffMs)
    {
        while (_rolling.First is { } first && first.Value.AtMs < cutoffMs)
            _rolling.RemoveFirst();
        while (_recent.First is { } first && first.Value.AtMs < cutoffMs)
            _recent.RemoveFirst();
    }

    public FrameSnapshot Snapshot(long nowMs)
    {
        lock (_gate)
        {
            TrimOlderThan(nowMs - RollingWindowMs);
            while (_recent.First is { } first && first.Value.AtMs < nowMs - 2_000)
                _recent.RemoveFirst();

            if (!_hasFrames) return new FrameSnapshot(null, null, null, null, 0);

            // If PresentMon has gone quiet (paused game / capture failure), report 0 FPS
            // rather than freezing the last value forever.
            if (nowMs - _lastFrameAtMs > 2_000)
                return new FrameSnapshot(0, null, RollingLow(1), RollingLow(0.1), _sessionFrames);

            var recent = _recent.Where(f => f.AtMs >= nowMs - 1_000).ToArray();
            double? fps = recent.Length > 0 ? recent.Length : 0;
            double? frameTime = recent.Length > 0 ? recent.Average(f => f.FrameTimeMs) : null;

            return new FrameSnapshot(fps, frameTime, RollingLow(1), RollingLow(0.1), _sessionFrames);
        }
    }

    private double? RollingLow(double worstPercent)
    {
        // worst 1% frame times => 99th percentile frame time
        if (_rolling.Count < 10) return null;
        var times = _rolling.Select(f => f.FrameTimeMs).ToArray();
        Array.Sort(times);
        var p = Math.Clamp(100 - worstPercent, 0, 100) / 100d;
        var idx = Math.Min(times.Length - 1, (int)Math.Ceiling((times.Length - 1) * p));
        var worstFrameTime = times[idx];
        return worstFrameTime > 0 ? 1000d / worstFrameTime : null;
    }

    public double? SessionAverageFps
    {
        get
        {
            lock (_gate)
                return _sessionFrames > 0 && _sessionFrameTimeSum > 0
                    ? 1000d / (_sessionFrameTimeSum / _sessionFrames)
                    : null;
        }
    }

    public double? SessionMedianFps
    {
        get
        {
            lock (_gate) return SessionPercentileFrameTime(50) is { } ft and > 0 ? 1000d / ft : null;
        }
    }

    /// <summary>FPS at the worst <paramref name="worstPercent"/> of frames (1 = 1% low).</summary>
    public double? SessionLowFps(double worstPercent)
    {
        lock (_gate)
        {
            var p = Math.Clamp(100 - worstPercent, 0, 100);
            return SessionPercentileFrameTime(p) is { } ft and > 0 ? 1000d / ft : null;
        }
    }

    private double? SessionPercentileFrameTime(double percentile)
    {
        if (_sessionFrames <= 0) return null;
        var target = Math.Ceiling(_sessionFrames * percentile / 100d);
        long seen = 0;
        for (var i = 0; i < _histogram.Length; i++)
        {
            seen += _histogram[i];
            if (seen >= target)
            {
                // Use the upper edge of the bucket; conservative but stable.
                return Math.Min(60_000, (i + 1) * BucketWidthMs);
            }
        }
        return null;
    }

    public long SessionFrameCount
    {
        get { lock (_gate) return _sessionFrames; }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _rolling.Clear();
            _recent.Clear();
            Array.Clear(_histogram);
            _sessionFrames = 0;
            _sessionFrameTimeSum = 0;
            _lastFrameAtMs = 0;
            _hasFrames = false;
        }
    }
}
