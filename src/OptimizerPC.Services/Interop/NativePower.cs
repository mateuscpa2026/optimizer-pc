using System.Runtime.InteropServices;
using System.Text;

namespace OptimizerPC.Services.Interop;

/// <summary>
/// Planos de energia via PowrProf. Somente leitura e troca de plano ativo:
/// nenhuma configuracao de firmware, overclock ou limite de energia e alterado.
/// </summary>
internal static class NativePower
{
    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerEnumerate(
        IntPtr rootPowerKey,
        IntPtr schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        byte[] buffer,
        ref uint bufferSize);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        byte[] buffer,
        ref uint bufferSize);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    private const uint AccessScheme = 0x00000010; // ACCESS_SCHEME
    private const uint ErrorSuccess = 0;

    internal sealed record PowerScheme(Guid Guid, string Name);

    internal static IReadOnlyList<PowerScheme> Enumerate()
    {
        var schemes = new List<PowerScheme>();
        uint index = 0;
        var buffer = new byte[16];

        while (true)
        {
            var size = (uint)buffer.Length;
            var status = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref size);

            if (status != ErrorSuccess)
            {
                break;
            }

            if (size < 16)
            {
                break;
            }

            var guid = new Guid(buffer.AsSpan(0, 16));
            schemes.Add(new PowerScheme(guid, ReadFriendlyName(guid)));
            index++;
        }

        return schemes;
    }

    internal static Guid? GetActiveScheme()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != ErrorSuccess || pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var bytes = new byte[16];
                Marshal.Copy(pointer, bytes, 0, 16);
                return new Guid(bytes);
            }
            finally
            {
                LocalFree(pointer);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static bool SetActiveScheme(Guid guid)
    {
        try
        {
            return PowerSetActiveScheme(IntPtr.Zero, ref guid) == ErrorSuccess;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ReadFriendlyName(Guid guid)
    {
        var buffer = new byte[512];
        var size = (uint)buffer.Length;

        if (PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != ErrorSuccess || size == 0)
        {
            return string.Empty;
        }

        var text = Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(size, (uint)buffer.Length));
        return text.TrimEnd('\0').Trim();
    }
}
