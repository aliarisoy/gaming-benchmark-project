using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RigSight.Windows.Games;

/// <summary>
/// Answers whether the tracked game is the window the player is actually looking at.
/// A minimized game, or one hidden behind another app, is not producing gameplay
/// frames; recording is paused until it comes back to the foreground.
/// </summary>
public static class GameWindowState
{
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public static bool IsActivelyPlayed(int processId)
    {
        if (processId <= 0) return true;

        try
        {
            if (ForegroundWindow.GetForegroundProcessId() != processId)
                return false;

            using var process = Process.GetProcessById(processId);
            var window = process.MainWindowHandle;
            if (window == IntPtr.Zero) return true;
            return IsWindowVisible(window) && !IsIconic(window);
        }
        catch
        {
            // If the window cannot be inspected, fail open rather than silently
            // pausing a session. The game watcher still owns game-exit handling.
            return true;
        }
    }
}
