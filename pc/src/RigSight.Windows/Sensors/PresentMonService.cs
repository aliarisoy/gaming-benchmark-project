using System.Diagnostics;
using System.Globalization;
using System.Text;
using RigSight.Core.Logging;
using RigSight.Core.Telemetry;

namespace RigSight.Windows.Sensors;

/// <summary>
/// Runs Intel PresentMon's console capture for one PID and feeds frame times into
/// <see cref="FrameTimeStats"/>. No injection, no overlay: PresentMon reads ETW.
/// </summary>
public sealed class PresentMonService : IAsyncDisposable
{
    private readonly FrameTimeStats _stats;
    private readonly object _gate = new();
    private Process? _process;
    private CancellationTokenSource? _cts;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private int _pid;
    private string? _lastError;

    public PresentMonService(FrameTimeStats stats)
    {
        _stats = stats;
    }

    public bool IsCapturing { get; private set; }
    public static bool IsAvailable => PresentMonLocator.Find() is not null;
    public string? ExecutablePath { get; private set; }
    public string? LastError { get { lock (_gate) return _lastError; } }
    public event Action<string>? CaptureError;
    public event Action? CaptureStopped;

    public bool TryStart(int processId, string? executablePath = null)
    {
        StopAsync().GetAwaiter().GetResult();
        _pid = processId;
        ExecutablePath = executablePath ?? PresentMonLocator.Find();
        if (ExecutablePath is null || !File.Exists(ExecutablePath))
        {
            SetError("PresentMon.exe was not found. FPS capture is disabled.");
            return false;
        }

        var sessionName = $"RigSight-{processId}-{Guid.NewGuid().ToString("N")[..6]}";
        var args = string.Join(' ',
            "--process_id", processId.ToString(CultureInfo.InvariantCulture),
            "--output_stdout",
            "--no_console_stats",
            "--exclude_dropped",
            "--v2_metrics",
            "--terminate_on_proc_exit",
            "--session_name", sessionName);

        var psi = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        try
        {
            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.Exited += (_, _) =>
            {
                IsCapturing = false;
                try { CaptureStopped?.Invoke(); } catch { /* ignore */ }
            };
            if (!process.Start())
            {
                SetError("PresentMon could not be started.");
                return false;
            }

            _process = process;
            _cts = new CancellationTokenSource();
            IsCapturing = true;
            SetError(null);
            _stdoutTask = Task.Run(() => ReadStdoutAsync(process, _cts.Token));
            _stderrTask = Task.Run(() => ReadStderrAsync(process, _cts.Token));
            Log.Info($"PresentMon started for pid {processId} (capture session {sessionName}).");
            return true;
        }
        catch (Exception ex)
        {
            SetError($"PresentMon failed to start: {ex.Message}");
            return false;
        }
    }

    private async Task ReadStdoutAsync(Process process, CancellationToken ct)
    {
        try
        {
            var header = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
            while (header is not null && !LooksLikeHeader(header))
            {
                header = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
            }

            if (header is null)
            {
                SetError("PresentMon returned no CSV data. The game may not be presenting frames yet.");
                return;
            }

            var columns = Csv.Split(header);
            var timeIndex = FindColumn(columns, "MsBetweenPresents");
            var cpuTimeIndex = FindColumn(columns, "CPUStartTime");
            var elapsedIndex = FindColumn(columns, "TimeInSeconds");
            if (timeIndex < 0 && cpuTimeIndex < 0 && elapsedIndex < 0)
            {
                SetError("PresentMon output has no recognizable frame-time column.");
                return;
            }

            var clock = Stopwatch.StartNew();
            double? previousElapsed = null;
            while (!ct.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;
                if (line.Length == 0) continue;

                var fields = Csv.Split(line);
                double frameTime = 0;
                if (timeIndex >= 0 && TryDouble(fields, timeIndex, out var ms) && ms > 0)
                {
                    frameTime = ms;
                }
                else if (elapsedIndex >= 0 && TryDouble(fields, elapsedIndex, out var seconds))
                {
                    if (previousElapsed is { } prev && seconds > prev)
                        frameTime = (seconds - prev) * 1000;
                    previousElapsed = seconds;
                }
                else if (cpuTimeIndex >= 0 && TryDouble(fields, cpuTimeIndex, out var cpuMs))
                {
                    if (previousElapsed is { } prev && cpuMs > prev)
                        frameTime = cpuMs - prev;
                    previousElapsed = cpuMs;
                }

                if (frameTime > 0)
                    _stats.AddFrame(frameTime, Environment.TickCount64);
            }
        }
        catch (OperationCanceledException) { /* expected on stop */ }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested) SetError($"PresentMon read failed: {ex.Message}");
        }
        finally
        {
            IsCapturing = false;
        }
    }

    private async Task ReadStderrAsync(Process process, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                Log.Warn($"PresentMon: {line}");
                if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("access denied", StringComparison.OrdinalIgnoreCase))
                {
                    SetError(line.Trim());
                }
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch { /* stderr is best-effort */ }
    }

    private static int FindColumn(string[] columns, string name)
    {
        for (var i = 0; i < columns.Length; i++)
            if (string.Equals(columns[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static bool LooksLikeHeader(string line)
        => line.Contains("Application", StringComparison.OrdinalIgnoreCase)
           && line.Contains("ProcessID", StringComparison.OrdinalIgnoreCase);

    private static bool TryDouble(string[] fields, int index, out double value)
    {
        value = 0;
        return index < fields.Length &&
               double.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void SetError(string? message)
    {
        lock (_gate) _lastError = message;
        if (!string.IsNullOrWhiteSpace(message))
        {
            Log.Warn(message);
            try { CaptureError?.Invoke(message); } catch { /* ignore */ }
        }
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var process = _process;
        _cts = null;
        _process = null;
        IsCapturing = false;

        try { cts?.Cancel(); } catch { /* ignore */ }
        if (process is not null)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch { /* already gone */ }
        }

        try
        {
            if (_stdoutTask is not null) await Task.WhenAny(_stdoutTask, Task.Delay(1500)).ConfigureAwait(false);
            if (_stderrTask is not null) await Task.WhenAny(_stderrTask, Task.Delay(500)).ConfigureAwait(false);
        }
        catch { /* ignore */ }

        process?.Dispose();
        cts?.Dispose();
        _stdoutTask = null;
        _stderrTask = null;
        if (_pid != 0) Log.Info($"PresentMon stopped for pid {_pid}.");
        _pid = 0;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}

internal static class PresentMonLocator
{
    public static string? Find()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe"),
            Path.Combine(AppContext.BaseDirectory, "PresentMon.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "presentmon", "PresentMon.exe"),
        };

        // Walk up from the build output to the repository root during development.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 7 && dir is not null; i++, dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "tools", "presentmon", "PresentMon.exe"));

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        candidates.Add(Path.Combine(programFiles, "Intel", "PresentMon", "PresentMon.exe"));
        candidates.Add(Path.Combine(programFiles, "Intel", "PresentMon", "PresentMon-Console.exe"));

        return candidates
            .Select(c => Path.GetFullPath(c))
            .FirstOrDefault(File.Exists);
    }
}

internal static class Csv
{
    /// <summary>Minimal RFC4180-ish splitter (quoted commas and doubled quotes).</summary>
    public static string[] Split(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',')
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }
}
