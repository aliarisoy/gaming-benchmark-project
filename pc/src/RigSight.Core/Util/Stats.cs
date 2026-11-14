using System.Globalization;

namespace RigSight.Core.Util;

public static class Stats
{
    public static double? Mean(IEnumerable<double?> values)
    {
        var sum = 0d;
        var n = 0;
        foreach (var v in values)
        {
            if (v is not { } d || double.IsNaN(d) || double.IsInfinity(d)) continue;
            sum += d;
            n++;
        }
        return n == 0 ? null : sum / n;
    }

    public static double? Max(IEnumerable<double?> values)
    {
        double? max = null;
        foreach (var v in values)
        {
            if (v is not { } d || double.IsNaN(d) || double.IsInfinity(d)) continue;
            if (max is null || d > max) max = d;
        }
        return max;
    }

    public static double? Median(IEnumerable<double?> values)
    {
        var list = values.Where(v => v is { } d && !double.IsNaN(d) && !double.IsInfinity(d))
            .Select(v => v!.Value).OrderBy(v => v).ToArray();
        return PercentileSorted(list, 50);
    }

    public static double? Percentile(IEnumerable<double> values, double percentile)
    {
        var list = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToArray();
        return PercentileSorted(list, percentile);
    }

    public static double? PercentileSorted(double[] sorted, double percentile)
    {
        if (sorted.Length == 0) return null;
        if (sorted.Length == 1) return sorted[0];
        var p = Math.Clamp(percentile, 0, 100) / 100d;
        var index = (sorted.Length - 1) * p;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        var weight = index - lower;
        return sorted[lower] * (1 - weight) + sorted[upper] * weight;
    }

    public static string Format(double? value, int decimals = 0)
        => value is null ? "—" : value.Value.ToString("F" + decimals, CultureInfo.InvariantCulture);
}
