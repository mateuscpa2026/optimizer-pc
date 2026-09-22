using System.Runtime.InteropServices;

namespace OptimizerPC.Services.Interop;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SHELLEXECUTEINFO
{
    public int cbSize;
    public uint fMask;
    public IntPtr hwnd;
    public string? lpVerb;
    public string? lpFile;
    public string? lpParameters;
    public string? lpDirectory;
    public int nShow;
    public IntPtr hInstApp;
    public IntPtr lpIDList;
    public string? lpClass;
    public IntPtr hkeyClass;
    public uint dwHotKey;
    public IntPtr hIcon;
    public IntPtr hProcess;
}

/// <summary>
/// Execucao de processos e solicitacao de elevacao pelo mecanismo oficial do Windows (UAC).
/// Nunca usa shell de linha de comando: os argumentos sao passados individualmente.
/// </summary>
internal static class NativeShell
{
    internal const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
    internal const uint SEE_MASK_NOASYNC = 0x00000100;
    internal const int SW_SHOWNORMAL = 1;
    internal const int SW_HIDE = 0;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO executeInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO queryInfo);

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    internal const uint SHERB_NOCONFIRMATION = 0x00000001;
    internal const uint SHERB_NOPROGRESSUI = 0x00000002;
    internal const uint SHERB_NOSOUND = 0x00000004;

    /// <summary>
    /// Inicia um processo. Quando <paramref name="elevate"/> for verdadeiro, solicita elevacao via UAC.
    /// </summary>
    internal static bool TryStart(string fileName, string? arguments, bool elevate, bool hidden, out string error)
        => TryStart(fileName, arguments, elevate, hidden, null, out error);

    /// <summary>
    /// Inicia um processo. Quando <paramref name="elevate"/> for verdadeiro, solicita elevacao via UAC.
    /// </summary>
    internal static bool TryStart(string fileName, string? arguments, bool elevate, bool hidden, string? workingDirectory, out string error)
    {
        error = string.Empty;

        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC,
            hwnd = IntPtr.Zero,
            lpVerb = elevate ? "runas" : null,
            lpFile = fileName,
            lpParameters = arguments,
            lpDirectory = workingDirectory,
            nShow = hidden ? SW_HIDE : SW_SHOWNORMAL
        };

        try
        {
            if (!ShellExecuteEx(ref info))
            {
                error = "ShellExecuteEx falhou com o codigo " + Marshal.GetLastWin32Error();
                return false;
            }

            if (info.hProcess != IntPtr.Zero)
            {
                CloseHandle(info.hProcess);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
