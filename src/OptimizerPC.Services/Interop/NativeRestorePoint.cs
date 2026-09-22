using System.Runtime.InteropServices;

namespace OptimizerPC.Services.Interop;

/// <summary>
/// Pontos de restauracao do Windows via srclient.
/// O aplicativo apenas solicita a criacao; quem decide se cria ou nao e o proprio Windows.
/// </summary>
internal static class NativeRestorePoint
{
    internal const int BeginSystemChange = 100;
    internal const int EndSystemChange = 101;
    internal const int ModifySettings = 12;
    internal const int ApplicationInstall = 0;

    internal const int MaxDescriptionLength = 256;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct RESTOREPOINTINFO
    {
        public int EventType;
        public int RestorePointType;
        public long SequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxDescriptionLength)]
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STATEMGRSTATUS
    {
        public uint Status;
        public long SequenceNumber;
    }

    [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SRSetRestorePoint(ref RESTOREPOINTINFO restorePointInfo, out STATEMGRSTATUS status);
}
