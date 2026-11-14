using RigSight.Core.Util;

namespace RigSight.Core.Tests;

public sealed class StatsTests
{
    [Fact]
    public void Mean_IgnoresNullsAndNonFiniteValues()
    {
        Assert.Equal(1.5, Stats.Mean([1, 2, null, double.NaN, double.PositiveInfinity]));
        Assert.Null(Stats.Mean([null, double.NaN]));
    }

    [Fact]
    public void Max_IgnoresNullsAndReturnsNullWhenEmpty()
    {
        Assert.Equal(9, Stats.Max([3, null, 9, double.NegativeInfinity]));
        Assert.Null(Stats.Max([]));
    }

    [Fact]
    public void Median_HandlesEvenAndOddCounts()
    {
        Assert.Equal(2, Stats.Median([3, 1, 2]));
        Assert.Equal(2.5, Stats.Median([4, 1, 3, 2]));
    }

    [Fact]
    public void Percentile_Interpolates()
    {
        Assert.Equal(3, Stats.Percentile([1, 2, 3, 4, 5], 50));
        Assert.Equal(2, Stats.Percentile([1, 2, 3, 4, 5], 25));
        Assert.Equal(4, Stats.Percentile([1, 2, 3, 4, 5], 75));
        Assert.Equal(1, Stats.Percentile([1, 2, 3, 4, 5], 0));
        Assert.Equal(5, Stats.Percentile([1, 2, 3, 4, 5], 100));
    }

    [Fact]
    public void Percentile_EmptyOrSingle()
    {
        Assert.Null(Stats.Percentile([], 50));
        Assert.Equal(7, Stats.Percentile([7], 99));
    }

    [Fact]
    public void Format_UsesInvariantCulture()
    {
        Assert.Equal("—", Stats.Format(null));
        Assert.Equal("12", Stats.Format(12.4));
        Assert.Equal("12.3", Stats.Format(12.34, 1));
    }
}
