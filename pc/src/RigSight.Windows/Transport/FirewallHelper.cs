using System.Diagnostics;
using RigSight.Core.Logging;

namespace RigSight.Windows.Transport;

/// <summary>
/// Creates a narrowly scoped Windows Firewall rule for the LAN WebSocket port.
/// The agent already runs elevated, so this needs no extra prompt.
/// </summary>
internal static class FirewallHelper
{
    private static readonly object Gate = new();
    private static readonly HashSet<int> EnsuredPorts = [];

    public static async Task EnsureInboundRuleAsync(int port)
    {
        lock (Gate)
        {
            if (!EnsuredPorts.Add(port)) return;
        }

        var ruleName = $"RigSight phone stream (TCP {port})";
        try
        {
            if (await RunNetshAsync($"advfirewall firewall show rule name=\"{ruleName}\""))
            {
                Log.Debug($"Firewall rule already present for TCP {port}.");
                return;
            }

            var added = await RunNetshAsync(
                $"advfirewall firewall add rule name=\"{ruleName}\" " +
                $"dir=in action=allow protocol=TCP localport={port} " +
                "profile=private,domain " +
                "description=\"Allows the RigSight phone app to stream telemetry over the local network.\"");

            if (added)
            {
                Log.Info($"Added Windows Firewall rule for TCP {port} (private/domain networks).");
            }
            else
            {
                lock (Gate) EnsuredPorts.Remove(port);
                Log.Warn($"Could not add Windows Firewall rule for TCP {port}. " +
                         "Phones may not be able to connect; add an inbound rule manually.");
            }
        }
        catch (Exception ex)
        {
            lock (Gate) EnsuredPorts.Remove(port);
            Log.Warn($"Windows Firewall rule setup failed: {ex.Message}");
        }
    }

    private static async Task<bool> RunNetshAsync(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return false;
        }

        if (process.ExitCode != 0)
        {
            var detail = (await stderr).Trim();
            if (detail.Length > 0) Log.Debug($"netsh: {detail}");
        }

        await stdout;
        return process.ExitCode == 0;
    }
}
