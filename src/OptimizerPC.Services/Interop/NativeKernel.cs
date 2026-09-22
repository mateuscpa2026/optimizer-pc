using System.Runtime.InteropServices;

namespace OptimizerPC.Services.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct FILETIME
{
    public uint LowDateTime;
    public uint HighDateTime;

    public readonly ulong ToUInt64() => ((ulong)HighDateTime << 32) | LowDateTime;

    public readonly TimeSpan ToTimeSpan() => TimeSpan.FromTicks((long)ToUInt64());
}

[StructLayout(LayoutKind.Sequential)]
internal struct MEMORYSTATUSEX
{
    public uint Length;
    public uint MemoryLoad;
    public ulong TotalPhysical;
    public ulong AvailablePhysical;
    public ulong TotalPageFile;
    public ulong AvailablePageFile;
    public ulong TotalVirtual;
    public ulong AvailableVirtual;
    public ulong AvailableExtendedVirtual;

    public static MEMORYSTATUSEX Create() => new()
    {
        Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct RTL_OSVERSIONINFOW
{
    public uint OSVersionInfoSize;
    public uint MajorVersion;
    public uint MinorVersion;
    public uint BuildNumber;
    public uint PlatformId;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string CSDVersion;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_POWER_STATUS
{
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public uint BatteryLifeTime;
    public uint BatteryFullLifeTime;
}

/// <summary>Wrappers finos de kernel32 usados pelo diagnostico e pelo monitor.</summary>
internal static class NativeKernel
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GlobalMemoryStatusEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern ulong GetTickCount64();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetDiskFreeSpaceEx(string directoryName, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetLogicalDrives();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint GetDriveType(string rootPathName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int GetVolumeInformation(
        string rootPathName,
        char[]? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        char[]? fileSystemNameBuffer,
        int fileSystemNameSize);

    [DllImport("ntdll.dll", SetLastError = true)]
    internal static extern int RtlGetVersion(ref RTL_OSVERSIONINFOW versionInformation);
}
