using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace OptimizerPC.Services.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESS_MEMORY_COUNTERS_EX
{
    public uint cb;
    public uint PageFaultCount;
    public UIntPtr PeakWorkingSetSize;
    public UIntPtr WorkingSetSize;
    public UIntPtr QuotaPeakPagedPoolUsage;
    public UIntPtr QuotaPagedPoolUsage;
    public UIntPtr QuotaPeakNonPagedPoolUsage;
    public UIntPtr QuotaNonPagedPoolUsage;
    public UIntPtr PagefileUsage;
    public UIntPtr PeakPagefileUsage;
    public UIntPtr PrivateUsage;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TOKEN_ELEVATION
{
    public int TokenIsElevated;
}

[StructLayout(LayoutKind.Sequential)]
internal struct IO_COUNTERS
{
    public ulong ReadOperationCount;
    public ulong WriteOperationCount;
    public ulong OtherOperationCount;
    public ulong ReadTransferCount;
    public ulong WriteTransferCount;
    public ulong OtherTransferCount;
}

/// <summary>Metricas de um processo lidas com um unico identificador aberto.</summary>
internal readonly record struct ProcessMetrics(ulong CpuTime100Ns, ulong IoTransferBytes, long PrivateBytes, long WorkingSetBytes);

/// <summary>
/// Acesso somente leitura a dados de processos e verificacao de elevacao do processo atual.
/// </summary>
internal static class NativeProcess
{
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint PROCESS_SET_INFORMATION = 0x0200;
    internal const uint PROCESS_TERMINATE = 0x0001;

    internal const uint TOKEN_QUERY = 0x0008;
    internal const int TokenElevation = 20;

    internal const uint BELOW_NORMAL_PRIORITY_CLASS = 0x00004000;
    internal const uint NORMAL_PRIORITY_CLASS = 0x00000020;
    internal const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000;
    internal const uint HIGH_PRIORITY_CLASS = 0x00000080;

    internal const uint IDLE_PRIORITY_CLASS = 0x00000040;
    internal const uint REALTIME_PRIORITY_CLASS = 0x00000100;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        int tokenInformationClass,
        ref TOKEN_ELEVATION tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetPriorityClass(SafeProcessHandle process, uint priorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetPriorityClass(SafeProcessHandle process);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessMemoryInfo(SafeProcessHandle process, ref PROCESS_MEMORY_COUNTERS_EX counters, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessTimes(SafeProcessHandle process, out FILETIME creationTime, out FILETIME exitTime, out FILETIME kernelTime, out FILETIME userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessIoCounters(SafeProcessHandle process, out IO_COUNTERS counters);

    /// <summary>
    /// Le tempo de CPU, volume de entrada/saida e memoria de um processo abrindo um unico identificador.
    /// </summary>
    internal static bool TryReadMetrics(int processId, out ProcessMetrics metrics)
    {
        metrics = default;

        try
        {
            using var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle.IsInvalid)
            {
                return false;
            }

            ulong cpuTime = 0;
            if (GetProcessTimes(handle, out _, out _, out var kernelTime, out var userTime))
            {
                cpuTime = kernelTime.ToUInt64() + userTime.ToUInt64();
            }

            ulong ioBytes = 0;
            if (GetProcessIoCounters(handle, out var io))
            {
                ioBytes = io.ReadTransferCount + io.WriteTransferCount + io.OtherTransferCount;
            }

            long privateBytes = 0;
            long workingSet = 0;

            var counters = new PROCESS_MEMORY_COUNTERS_EX { cb = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX>() };
            if (GetProcessMemoryInfo(handle, ref counters, counters.cb))
            {
                privateBytes = (long)counters.PrivateUsage.ToUInt64();
                workingSet = (long)counters.WorkingSetSize.ToUInt64();
            }

            metrics = new ProcessMetrics(cpuTime, ioBytes, privateBytes, workingSet);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Indica se o processo atual esta elevado (executando como administrador).</summary>
    internal static bool IsCurrentProcessElevated()
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var token))
            {
                return false;
            }

            using (token)
            {
                var elevation = new TOKEN_ELEVATION();
                if (!GetTokenInformation(token, TokenElevation, ref elevation, (uint)Marshal.SizeOf<TOKEN_ELEVATION>(), out _))
                {
                    return false;
                }

                return elevation.TokenIsElevated != 0;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static string GetExecutablePath(int processId)
    {
        try
        {
            using var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle.IsInvalid)
            {
                return string.Empty;
            }

            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    internal static bool TrySetPriority(int processId, uint priorityClass)
    {
        try
        {
            using var handle = OpenProcess(PROCESS_SET_INFORMATION, false, processId);
            return !handle.IsInvalid && SetPriorityClass(handle, priorityClass);
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static bool TryGetPrivateBytes(int processId, out long privateBytes)
    {
        privateBytes = 0;

        try
        {
            using var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle.IsInvalid)
            {
                return false;
            }

            var counters = new PROCESS_MEMORY_COUNTERS_EX();
            if (!GetProcessMemoryInfo(handle, ref counters, (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX>()))
            {
                return false;
            }

            privateBytes = (long)counters.PrivateUsage.ToUInt64();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static bool TryGetDetectedPriority(int processId, out uint priorityClass)
    {
        priorityClass = 0;

        try
        {
            using var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle.IsInvalid)
            {
                return false;
            }

            priorityClass = GetPriorityClass(handle);
            return priorityClass != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
