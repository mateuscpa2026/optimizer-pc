using System.Runtime.InteropServices;
using System.Text;

namespace OptimizerPC.Services.Interop;

/// <summary>
/// Leitor da tabela SMBIOS exposta pelo firmware (GetSystemFirmwareTable).
/// Somente leitura, sem WMI. Quando o firmware nao expoe a tabela, os campos ficam indisponiveis.
/// </summary>
internal static class NativeSmbios
{
    private const uint RawSmbiosProvider = 0x52534D42; // 'RSMB'

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(uint firmwareTableProviderSignature, uint firmwareTableId, IntPtr firmwareTableBuffer, uint bufferSize);

    internal sealed class SmbiosTable
    {
        public byte MajorVersion { get; init; }

        public byte MinorVersion { get; init; }

        public List<SmbiosStructure> Structures { get; } = new();
    }

    internal sealed class SmbiosStructure
    {
        public byte Type { get; init; }

        public byte[] Formatted { get; init; } = Array.Empty<byte>();

        public IReadOnlyList<string> Strings { get; init; } = Array.Empty<string>();

        public string GetString(byte index)
        {
            if (index == 0 || index > Strings.Count)
            {
                return string.Empty;
            }

            return Strings[index - 1].Trim();
        }

        public byte GetByte(int offset) => offset >= 0 && offset < Formatted.Length ? Formatted[offset] : (byte)0;

        public ushort GetWord(int offset) => offset >= 0 && offset + 2 <= Formatted.Length
            ? BitConverter.ToUInt16(Formatted, offset)
            : (ushort)0;

        public uint GetDword(int offset) => offset >= 0 && offset + 4 <= Formatted.Length
            ? BitConverter.ToUInt32(Formatted, offset)
            : 0u;

        public ulong GetQword(int offset) => offset >= 0 && offset + 8 <= Formatted.Length
            ? BitConverter.ToUInt64(Formatted, offset)
            : 0ul;
    }

    internal static SmbiosTable? Read()
    {
        try
        {
            var size = GetSystemFirmwareTable(RawSmbiosProvider, 0, IntPtr.Zero, 0);
            if (size == 0 || size > 4 * 1024 * 1024)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetSystemFirmwareTable(RawSmbiosProvider, 0, buffer, size) != size)
                {
                    return null;
                }

                var raw = new byte[size];
                Marshal.Copy(buffer, raw, 0, (int)size);
                return Parse(raw);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static SmbiosTable? Parse(byte[] raw)
    {
        // RawSMBIOSData: Used20CallingMethod (1) + MajorVersion (1) + MinorVersion (1) + DmiRevision (1) + Length (4) + tabela.
        if (raw.Length < 8)
        {
            return null;
        }

        var table = new SmbiosTable
        {
            MajorVersion = raw[1],
            MinorVersion = raw[2]
        };

        var length = (int)BitConverter.ToUInt32(raw, 4);
        var offset = 8;
        var end = Math.Min(raw.Length, offset + length);

        while (offset + 4 <= end)
        {
            var type = raw[offset];
            if (type == 127)
            {
                break;
            }

            var formattedLength = raw[offset + 1];
            if (formattedLength < 4 || offset + formattedLength > end)
            {
                break;
            }

            // A estrutura completa (incluindo o cabecalho tipo/comprimento/handle) e
            // preservada para que os deslocamentos sigam exatamente a especificacao SMBIOS.
            var formatted = new byte[formattedLength];
            Array.Copy(raw, offset, formatted, 0, formattedLength);

            var cursor = offset + formattedLength;
            var strings = new List<string>();

            while (cursor < end && raw[cursor] != 0)
            {
                var start = cursor;
                while (cursor < end && raw[cursor] != 0)
                {
                    cursor++;
                }

                var count = cursor - start;
                strings.Add(Encoding.ASCII.GetString(raw, start, count));
                cursor++;
            }

            // Terminador duplo da tabela de strings.
            cursor++;

            table.Structures.Add(new SmbiosStructure
            {
                Type = type,
                Formatted = formatted,
                Strings = strings
            });

            offset = cursor;
        }

        return table;
    }
}
