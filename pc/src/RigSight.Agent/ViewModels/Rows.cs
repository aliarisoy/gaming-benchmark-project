using CommunityToolkit.Mvvm.ComponentModel;
using RigSight.Core.Models;

namespace RigSight.Agent.ViewModels;

public sealed record NavItem(string Glyph, string Label);

public sealed record SampleIntervalOption(int Value, string Label);

public sealed record ActivityItem(string Time, string Message, bool IsWarning, bool IsError);

public sealed partial class GameRow : ObservableObject
{
    public GameRow(GameProfile game)
    {
        Game = game;
    }

    public GameProfile Game { get; }
    public string Id => Game.Id;
    public string Name => Game.Name;
    public string ExeName => Game.ExeName;
    public string ExePath => Game.ExePath ?? Game.ExeName;
    public string Initials => Game.Initials;
    public string LastPlayedLabel => Game.LastPlayedAt is { } at
        ? $"Last played {at.ToLocalTime():MMM d, HH:mm}"
        : "Not played yet";

    [ObservableProperty]
    private bool _isRunning;
}

public sealed class SessionRow
{
    public SessionRow(SessionSummary summary)
    {
        Summary = summary;
    }

    public SessionSummary Summary { get; }
    public string Id => Summary.Id;
    public string GameName => Summary.GameName;
    public string Initials
    {
        get
        {
            var words = Summary.GameName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "?";
            if (words.Length == 1) return words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant();
            return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        }
    }

    public string DateLabel => Summary.StartedAt.ToLocalTime().ToString("MMM d · HH:mm");
    public string DurationLabel => Summary.DurationLabel;
    public string AvgFpsText => Format.Number(Summary.AvgFps);
    public string MedianFpsText => Format.Number(Summary.MedianFps);
    public string Low1Text => Format.Number(Summary.Low1Fps);
    public string Low01Text => Format.Number(Summary.Low01Fps, "0.0");
    public string FrameTimeText => Format.Number(Summary.AvgFrameTimeMs, "0.0");
    public string GpuTempText => Format.Number(Summary.MaxGpuTempC);
    public string CpuTempText => Format.Number(Summary.MaxCpuTempC);
    public string GpuPowerText => Format.Number(Summary.MaxGpuPowerW);
    public string CpuPowerText => Format.Number(Summary.MaxCpuPowerW);
    public string GpuLoadText => Format.Number(Summary.AvgGpuLoadPct);
    public string CpuLoadText => Format.Number(Summary.AvgCpuLoadPct);
    public string VramText => Summary.MaxVramUsedMb is { } mb ? $"{mb / 1024:0.0} GB" : "—";
    public string SamplesText => $"{Summary.SampleCount} samples";
    public string StopReasonLabel => Summary.StopReason switch
    {
        StopReasons.User => "Stopped on PC",
        StopReasons.Phone => "Stopped on phone",
        StopReasons.GameExit => "Game exited",
        StopReasons.AgentShutdown => "Agent closed",
        StopReasons.Interrupted => "Interrupted",
        StopReasons.Demo => "Demo",
        _ => Summary.StopReason,
    };
}

public sealed class DeviceRow
{
    public DeviceRow(PairedDevice device)
    {
        Device = device;
    }

    public PairedDevice Device { get; }
    public string Id => Device.Id;
    public string Name => string.IsNullOrWhiteSpace(Device.Name) ? "Android phone" : Device.Name;
    public string PairedLabel => $"Paired {Device.PairedAt.ToLocalTime():MMM d, HH:mm}";
    public string LastSeenLabel => Device.LastSeenAt is { } seen
        ? $"Last seen {seen.ToLocalTime():MMM d, HH:mm}"
        : "Not connected yet";
}

public static class Format
{
    public static string Number(double? value, string format = "0")
        => value is { } v ? v.ToString(format, System.Globalization.CultureInfo.InvariantCulture) : "—";

    public static string Memory(double? usedMb, double? totalMb)
    {
        if (usedMb is not { } used) return "—";
        var usedText = (used / 1024).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return totalMb is > 0
            ? $"{usedText} / {(totalMb.Value / 1024).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} GB"
            : $"{usedText} GB";
    }

    public static string Elapsed(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }
}
