using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OptimizerPC.Services.Interop;

internal enum SC_ACTION_TYPE
{
    SC_ACTION_NONE = 0,
    SC_ACTION_RESTART = 1,
    SC_ACTION_REBOOT = 2,
    SC_ACTION_RUN_COMMAND = 3
}

[StructLayout(LayoutKind.Sequential)]
internal struct SERVICE_STATUS_PROCESS
{
    public uint ServiceType;
    public uint CurrentState;
    public uint ControlsAccepted;
    public uint Win32ExitCode;
    public uint ServiceSpecificExitCode;
    public uint CheckPoint;
    public uint WaitHint;
    public uint ProcessId;
    public uint ServiceFlags;
}

/// <summary>
/// Gerenciador de servicos do Windows (somente leitura de configuracao e troca de modo de inicio de servicos permitidos).
/// </summary>
internal static class NativeServices
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SERVICE_DESCRIPTION
    {
        public string? Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SERVICE_DELAYED_AUTO_START_INFO
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsDelayedAutoStart;
    }

    internal const uint SC_MANAGER_CONNECT = 0x0001;
    internal const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;

    internal const uint SERVICE_QUERY_CONFIG = 0x0001;
    internal const uint SERVICE_CHANGE_CONFIG = 0x0002;
    internal const uint SERVICE_QUERY_STATUS = 0x0004;
    internal const uint SERVICE_START = 0x0010;
    internal const uint SERVICE_STOP = 0x0020;

    internal const uint SERVICE_WIN32 = 0x00000030;

    internal const uint SERVICE_ACTIVE = 0x00000001;
    internal const uint SERVICE_INACTIVE = 0x00000002;
    internal const uint SERVICE_STATE_ALL = SERVICE_ACTIVE | SERVICE_INACTIVE;

    internal const uint SC_STATUS_PROCESS_INFO = 0;

    internal const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    internal const uint SERVICE_AUTO_START = 0x00000002;
    internal const uint SERVICE_DEMAND_START = 0x00000003;
    internal const uint SERVICE_DISABLED = 0x00000004;
    internal const uint SERVICE_BOOT_START = 0x00000000;
    internal const uint SERVICE_SYSTEM_START = 0x00000001;

    internal const uint SERVICE_CONFIG_DESCRIPTION = 1;
    internal const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;

    internal const uint SERVICE_ACCEPT_STOP = 0x00000001;

    internal const uint SERVICE_RUNNING = 0x00000004;
    internal const uint SERVICE_STOPPED = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ENUM_SERVICE_STATUS_PROCESS
    {
        public string? ServiceName;
        public string? DisplayName;
        public SERVICE_STATUS_PROCESS ServiceStatusProcess;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeWaitHandle OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeWaitHandle OpenService(SafeWaitHandle serviceManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumServicesStatusEx(
        SafeWaitHandle serviceManager,
        int infoLevel,
        uint serviceType,
        uint serviceState,
        IntPtr buffer,
        uint bufferSize,
        out uint bytesNeeded,
        out uint servicesReturned,
        ref uint resumeHandle,
        string? groupName);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryServiceStatusEx(
        SafeWaitHandle service,
        int infoLevel,
        IntPtr buffer,
        uint bufferSize,
        out uint bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryServiceConfig2(
        SafeWaitHandle service,
        uint infoLevel,
        IntPtr buffer,
        uint bufferSize,
        out uint bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeServiceConfig(
        SafeWaitHandle service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeServiceConfig2(
        SafeWaitHandle service,
        uint infoLevel,
        IntPtr info);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool StartService(SafeWaitHandle service, uint numServiceArgs, string[]? serviceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ControlService(SafeWaitHandle service, uint control, IntPtr status);

    internal const uint SERVICE_CONTROL_STOP = 0x00000001;
}
