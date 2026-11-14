using LibreHardwareMonitor.Hardware;
using RigSight.Core.Logging;

namespace RigSight.Windows.Sensors;

/// <summary>
/// Polls LibreHardwareMonitor on a background thread. The LHM driver is only
/// available when the agent is elevated; when it fails we keep the agent alive and
/// merge in NVML/OS fallbacks instead.
/// </summary>
public sealed class HardwareSensorService : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly NvmlProvider _nvml = new();
    private readonly Thread _thread;
    private Computer? _computer;
    private IHardware? _cpu;
    private IHardware? _gpu;
    private IHardware? _memory;
    private HardwareSnapshot _snapshot = new() { Warning = "Sensors warming up…" };
    private DateTimeOffset _lastLhmWarning = DateTimeOffset.MinValue;

    public HardwareSensorService()
    {
        _thread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name = "RigSight hardware sensors",
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
    }

    public HardwareSnapshot Latest
    {
        get { lock (_gate) return _snapshot.Clone(); }
    }

    public bool IsReady { get; private set; }

    private void PollLoop()
    {
        TryOpenLhm();
        _nvml.TryInitialize();

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                PollOnce();
            }
            catch (Exception ex)
            {
                Log.Warn($"Sensor poll failed: {ex.Message}");
            }

            try
            {
                _cts.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(900));
            }
            catch
            {
                break;
            }
        }
    }

    private void TryOpenLhm()
    {
        try
        {
            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false,
                IsStorageEnabled = false,
                IsNetworkEnabled = false,
            };
            computer.Open();

            _cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            _gpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia)
                   ?? computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd)
                   ?? computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);
            _memory = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
            _computer = computer;

            Log.Info($"LibreHardwareMonitor opened: CPU={_cpu?.Name ?? "n/a"}, GPU={_gpu?.Name ?? "n/a"}.");
        }
        catch (Exception ex)
        {
            Log.Warn($"LibreHardwareMonitor unavailable; using safe mode. {ex.Message}");
            _computer = null;
        }

        if (!_nvml.IsAvailable) _nvml.TryInitialize();
    }

    private void PollOnce()
    {
        var result = new HardwareSnapshot();

        if (_computer is not null)
        {
            try
            {
                foreach (var hw in new[] { _cpu, _gpu, _memory })
                {
                    if (hw is null) continue;
                    hw.Update();
                    foreach (var sub in hw.SubHardware) sub.Update();
                }
            }
            catch (Exception ex)
            {
                if (DateTimeOffset.UtcNow - _lastLhmWarning > TimeSpan.FromSeconds(30))
                {
                    _lastLhmWarning = DateTimeOffset.UtcNow;
                    Log.Warn($"LHM update failed: {ex.Message}");
                }
            }

            ReadCpu(result);
            ReadGpu(result);
            ReadMemory(result);
            // LHM can open without elevation but still expose no CPU temperature
            // or package power. Report full sensors only when those values exist.
            result.FullSensors = result.CpuTempC is not null || result.CpuPowerW is not null;
        }

        if (!_nvml.IsAvailable || result.GpuLoadPct is null)
        {
            if (_nvml.TryRead(out var nv))
            {
                result.GpuLoadPct ??= nv.GpuLoadPct;
                result.GpuTempC ??= nv.GpuTempC;
                result.GpuPowerW ??= nv.GpuPowerW;
                result.GpuClockMhz ??= nv.GpuClockMhz;
                result.VramUsedMb ??= nv.VramUsedMb;
                result.VramTotalMb ??= nv.VramTotalMb;
                result.GpuName ??= nv.GpuName;
                result.GpuAvailable = true;
            }
        }

        result.CpuLoadPct ??= OsMetrics.CpuLoadPct();
        if (result.RamTotalMb is null)
        {
            OsMetrics.Ram(out var used, out var total);
            result.RamUsedMb = used;
            result.RamTotalMb = total;
        }

        result.CpuName ??= _cpu?.Name;
        result.GpuName ??= _gpu?.Name;
        if (result.GpuTempC is not null || result.GpuLoadPct is not null) result.GpuAvailable = true;

        if (result.FullSensors)
            result.Warning = null;
        else
            result.Warning = "Safe mode: CPU temperature and power need administrator access to the sensor driver.";

        lock (_gate) _snapshot = result;
        IsReady = true;
    }

    private void ReadCpu(HardwareSnapshot result)
    {
        if (_cpu is null) return;
        result.CpuName = _cpu.Name;
        result.CpuTempC =
            Find(_cpu, SensorType.Temperature, "CPU Package") ??
            Find(_cpu, SensorType.Temperature, "Core Average") ??
            Find(_cpu, SensorType.Temperature, "Core Max") ??
            Find(_cpu, SensorType.Temperature, "Tctl") ??
            Find(_cpu, SensorType.Temperature, "Tdie");
        result.CpuPowerW =
            Find(_cpu, SensorType.Power, "CPU Package") ??
            Find(_cpu, SensorType.Power, "Package") ??
            Find(_cpu, SensorType.Power, "CPU Cores");
        result.CpuLoadPct = Find(_cpu, SensorType.Load, "CPU Total") ?? Find(_cpu, SensorType.Load, "CPU Core");

        var clocks = _cpu.Sensors
            .Where(s => s.SensorType == SensorType.Clock && s.Value is > 0 &&
                        s.Name.Contains("Core #", StringComparison.OrdinalIgnoreCase) &&
                        !s.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase))
            .Select(s => (double)s.Value!.Value)
            .ToArray();
        if (clocks.Length > 0) result.CpuClockMhz = Math.Round(clocks.Average());
    }

    private void ReadGpu(HardwareSnapshot result)
    {
        if (_gpu is null) return;
        result.GpuName = _gpu.Name;
        result.GpuTempC = Find(_gpu, SensorType.Temperature, "GPU Core");
        result.GpuHotspotC =
            Find(_gpu, SensorType.Temperature, "GPU Hot Spot") ??
            Find(_gpu, SensorType.Temperature, "Hot Spot");
        result.GpuLoadPct = Find(_gpu, SensorType.Load, "GPU Core");
        result.GpuPowerW = Find(_gpu, SensorType.Power, "GPU Power") ?? Find(_gpu, SensorType.Power, "GPU");
        result.GpuClockMhz = Find(_gpu, SensorType.Clock, "GPU Core");
        result.VramUsedMb =
            Find(_gpu, SensorType.SmallData, "GPU Memory Used") ??
            Find(_gpu, SensorType.SmallData, "D3D Dedicated Memory Used");
        result.VramTotalMb =
            Find(_gpu, SensorType.SmallData, "GPU Memory Total") ??
            Find(_gpu, SensorType.SmallData, "D3D Dedicated Memory Total");
        if (result.GpuTempC is not null || result.GpuLoadPct is not null) result.GpuAvailable = true;
    }

    private void ReadMemory(HardwareSnapshot result)
    {
        if (_memory is null) return;
        // LHM Data sensors are GB.
        var usedGb = Find(_memory, SensorType.Data, "Memory Used");
        var availableGb = Find(_memory, SensorType.Data, "Memory Available");
        if (usedGb is not null) result.RamUsedMb = usedGb * 1024d;
        if (usedGb is not null && availableGb is not null) result.RamTotalMb = (usedGb + availableGb) * 1024d;
    }

    private static double? Find(IHardware hardware, SensorType type, string namePrefix)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType != type || sensor.Value is not { } value) continue;
            if (sensor.Name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                return Math.Round(value, 2);
        }
        return null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _thread.Join(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        try { _computer?.Close(); } catch { /* ignore */ }
        _nvml.Dispose();
        _cts.Dispose();
    }
}
