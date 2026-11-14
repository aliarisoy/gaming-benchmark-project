namespace RigSight.Windows.Sensors;

public sealed class HardwareSnapshot
{
    public double? CpuLoadPct { get; set; }
    public double? CpuTempC { get; set; }
    public double? CpuPowerW { get; set; }
    public double? CpuClockMhz { get; set; }

    public double? GpuLoadPct { get; set; }
    public double? GpuTempC { get; set; }
    public double? GpuHotspotC { get; set; }
    public double? GpuPowerW { get; set; }
    public double? GpuClockMhz { get; set; }

    public double? VramUsedMb { get; set; }
    public double? VramTotalMb { get; set; }
    public double? RamUsedMb { get; set; }
    public double? RamTotalMb { get; set; }

    public string? CpuName { get; set; }
    public string? GpuName { get; set; }

    public bool FullSensors { get; set; }
    public bool GpuAvailable { get; set; }
    public string? Warning { get; set; }

    public HardwareSnapshot Clone() => (HardwareSnapshot)MemberwiseClone();
}
