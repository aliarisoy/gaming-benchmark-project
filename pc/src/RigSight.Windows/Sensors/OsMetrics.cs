using System.Runtime.InteropServices;
using RigSight.Core.Logging;

namespace RigSight.Windows.Sensors;

/// <summary>Cheap OS-wide fallbacks that never load a kernel driver.</summary>
internal static class OsMetrics
{
    private static ulong _lastIdle;
    private static ulong _lastKernel;
    private static ulong _lastUser;

    public static double? CpuLoadPct()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var idleTicks = ToUInt64(idle);
        var kernelTicks = ToUInt64(kernel);
        var userTicks = ToUInt64(user);

        var idleDelta = idleTicks - _lastIdle;
        var totalDelta = (kernelTicks - _lastKernel) + (userTicks - _lastUser);
        _lastIdle = idleTicks;
        _lastKernel = kernelTicks;
        _lastUser = userTicks;
        if (totalDelta == 0) return null;
        var busy = totalDelta > idleDelta ? totalDelta - idleDelta : 0;
        return Math.Clamp(busy * 100d / totalDelta, 0, 100);
    }

    public static void Ram(out double usedMb, out double totalMb)
    {
        var status = new MemoryStatusEx();
        status.Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        if (GlobalMemoryStatusEx(ref status))
        {
            totalMb = status.TotalPhys / 1024d / 1024d;
            usedMb = (status.TotalPhys - status.AvailPhys) / 1024d / 1024d;
        }
        else
        {
            usedMb = 0;
            totalMb = 0;
        }
    }

    private static ulong ToUInt64(FileTime time) => ((ulong)time.HighDateTime << 32) | time.LowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
