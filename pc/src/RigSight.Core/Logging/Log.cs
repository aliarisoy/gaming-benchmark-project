namespace RigSight.Core.Logging;

public enum LogLevel { Debug, Info, Warn, Error }

public static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    public static event Action<string>? LineWritten;

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message, Exception? ex = null) =>
        Write(LogLevel.Error, ex is null ? message : $"{message} :: {ex}");

    private static void Write(LogLevel level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {message}";
        System.Diagnostics.Debug.WriteLine(line);
        lock (Gate)
        {
            try
            {
                _path ??= Path.Combine(Storage.RigSightPaths.LogsDirectory, $"agent-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never take down the agent.
            }
        }

        try { LineWritten?.Invoke(line); } catch { /* ignore listeners */ }
    }
}
