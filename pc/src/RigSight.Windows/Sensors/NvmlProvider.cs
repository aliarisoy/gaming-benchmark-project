using System.Runtime.InteropServices;
using RigSight.Core.Logging;

namespace RigSight.Windows.Sensors;

/// <summary>
/// Small, dependency-free NVIDIA telemetry path used when LibreHardwareMonitor's
/// driver is unavailable (safe mode). NVML ships with the NVIDIA driver.
/// </summary>
public sealed class NvmlProvider : IDisposable
{
    private delegate int InitDelegate();
    private delegate int ShutdownDelegate();
    private delegate int CountDelegate(out uint count);
    private delegate int HandleByIndexDelegate(uint index, out IntPtr handle);
    private delegate int TempDelegate(IntPtr device, uint sensorType, out uint temp);
    private delegate int PowerDelegate(IntPtr device, out uint milliwatts);
    private delegate int UtilizationDelegate(IntPtr device, out NvmlUtilization utilization);
    private delegate int ClockDelegate(IntPtr device, uint clockType, out uint mhz);
    private delegate int MemoryDelegate(IntPtr device, out NvmlMemory memory);
    private delegate int NameDelegate(IntPtr device, byte[] name, uint length);

    private readonly object _gate = new();
    private IntPtr _library;
    private IntPtr _device;
    private InitDelegate? _init;
    private ShutdownDelegate? _shutdown;
    private TempDelegate? _getTemp;
    private PowerDelegate? _getPower;
    private UtilizationDelegate? _getUtilization;
    private ClockDelegate? _getClock;
    private MemoryDelegate? _getMemory;
    private NameDelegate? _getName;
    private bool _initialized;

    public bool IsAvailable { get; private set; }
    public string? GpuName { get; private set; }

    public bool TryInitialize()
    {
        lock (_gate)
        {
            if (_initialized) return IsAvailable;
            _initialized = true;

            foreach (var candidate in new[]
                     {
                         "nvml.dll",
                         Path.Combine(Environment.SystemDirectory, "nvml.dll"),
                         @"C:\Program Files\NVIDIA Corporation\NVSMI\nvml.dll",
                     })
            {
                try
                {
                    if (NativeLibrary.TryLoad(candidate, out _library)) break;
                }
                catch { /* try next */ }
            }

            if (_library == IntPtr.Zero)
            {
                Log.Warn("NVML not found; NVIDIA safe-mode telemetry unavailable.");
                return false;
            }

            try
            {
                _init = Get<InitDelegate>("nvmlInit_v2");
                _shutdown = Get<ShutdownDelegate>("nvmlShutdown");
                var countFn = Get<CountDelegate>("nvmlDeviceGetCount_v2");
                var handleFn = Get<HandleByIndexDelegate>("nvmlDeviceGetHandleByIndex_v2");
                _getTemp = Get<TempDelegate>("nvmlDeviceGetTemperature");
                _getPower = Get<PowerDelegate>("nvmlDeviceGetPowerUsage");
                _getUtilization = Get<UtilizationDelegate>("nvmlDeviceGetUtilizationRates");
                _getClock = Get<ClockDelegate>("nvmlDeviceGetClockInfo");
                _getMemory = Get<MemoryDelegate>("nvmlDeviceGetMemoryInfo");
                _getName = Get<NameDelegate>("nvmlDeviceGetName");

                if (_init() != 0 || countFn(out var count) != 0 || count == 0 || handleFn(0, out _device) != 0)
                {
                    Log.Warn("NVML initialized but no accessible NVIDIA GPU was found.");
                    return false;
                }

                var nameBuffer = new byte[128];
                if (_getName(_device, nameBuffer, (uint)nameBuffer.Length) == 0)
                {
                    var end = Array.IndexOf(nameBuffer, (byte)0);
                    GpuName = System.Text.Encoding.ASCII.GetString(nameBuffer, 0, end < 0 ? nameBuffer.Length : end);
                }

                IsAvailable = true;
                Log.Info($"NVML ready: {GpuName ?? "NVIDIA GPU"}.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"NVML could not be initialized: {ex.Message}");
                IsAvailable = false;
                return false;
            }
        }
    }

    private T Get<T>(string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    public bool TryRead(out HardwareSnapshot snapshot)
    {
        snapshot = new HardwareSnapshot();
        lock (_gate)
        {
            if (!IsAvailable || _device == IntPtr.Zero) return false;
            var result = new HardwareSnapshot { GpuAvailable = true, GpuName = GpuName };
            var ok = false;

            if (_getTemp is not null && _getTemp(_device, 0, out var temp) == 0)
            {
                result.GpuTempC = temp;
                ok = true;
            }
            if (_getPower is not null && _getPower(_device, out var mw) == 0)
            {
                result.GpuPowerW = mw / 1000d;
                ok = true;
            }
            if (_getUtilization is not null && _getUtilization(_device, out var util) == 0)
            {
                result.GpuLoadPct = util.Gpu;
                ok = true;
            }
            if (_getClock is not null && _getClock(_device, 0, out var clock) == 0)
            {
                result.GpuClockMhz = clock;
                ok = true;
            }
            if (_getMemory is not null && _getMemory(_device, out var memory) == 0)
            {
                result.VramTotalMb = memory.Total / 1024d / 1024d;
                result.VramUsedMb = memory.Used / 1024d / 1024d;
                ok = true;
            }

            snapshot = result;
            return ok;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _shutdown?.Invoke(); } catch { /* ignore */ }
            if (_library != IntPtr.Zero)
            {
                NativeLibrary.Free(_library);
                _library = IntPtr.Zero;
            }
            IsAvailable = false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }
}
