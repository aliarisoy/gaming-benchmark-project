using RigSight.Core.Telemetry;

namespace RigSight.Core.Tests;

public sealed class DemoSampleGeneratorTests
{
    [Fact]
    public void Generator_IsDeterministicForSameElapsedTimes()
    {
        var first = new DemoSampleGenerator();
        var second = new DemoSampleGenerator();

        for (var i = 0; i < 30; i++)
        {
            var elapsed = i * 500L;
            var a = first.Next(elapsed);
            var b = second.Next(elapsed);
            Assert.Equal(a.T, b.T);
            Assert.Equal(a.Fps, b.Fps);
            Assert.Equal(a.GpuTempC, b.GpuTempC);
        }
    }

    [Fact]
    public void Generator_ProducesPlausibleTelemetry()
    {
        var generator = new DemoSampleGenerator();
        var sample = generator.Next(5_000);

        Assert.Equal(5_000, sample.T);
        Assert.InRange(sample.Fps!.Value, 100, 170);
        Assert.InRange(sample.FrameTimeMs!.Value, 5, 10);
        Assert.InRange(sample.GpuLoadPct!.Value, 0, 100);
        Assert.InRange(sample.CpuLoadPct!.Value, 0, 100);
        Assert.InRange(sample.GpuTempC!.Value, 50, 90);
        Assert.InRange(sample.CpuTempC!.Value, 40, 90);
        Assert.Equal(10_240, sample.VramTotalMb);
        Assert.Equal(32_768, sample.RamTotalMb);
    }
}
