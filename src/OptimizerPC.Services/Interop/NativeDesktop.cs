using System.Runtime.InteropServices;

namespace OptimizerPC.Services.Interop;

/// <summary>
/// Avisa as janelas abertas que uma preferencia do sistema mudou. Usado depois de
/// alterar os efeitos visuais: o Explorer e os programas abertos recarregam o valor
/// sem exigir reinicio. A chamada tem tempo limite para nunca travar a interface.
/// </summary>
internal static class NativeDesktop
{
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint TimeoutMilliseconds = 1000;

    private static readonly IntPtr HwndBroadcast = new(0xffff);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    /// <summary>Notifica uma area de configuracao alterada (ex.: "VisualEffects").</summary>
    internal static void NotifySettingChange(string area)
    {
        try
        {
            SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, area, SmtoAbortIfHung, TimeoutMilliseconds, out _);
        }
        catch (Exception)
        {
            // Sem resposta da janela de broadcast: o valor ja esta gravado no registro
            // e passa a valer naturalmente na proxima sessao.
        }
    }
}
