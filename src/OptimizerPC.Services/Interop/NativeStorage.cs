using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OptimizerPC.Services.Interop;

internal enum STORAGE_PROPERTY_ID
{
    StorageDeviceProperty = 0,
    StorageAdapterProperty = 1,
    StorageDeviceSeekPenaltyProperty = 7,
    StorageDeviceTrimProperty = 8,
    StorageDeviceProtocolSpecificProperty = 49
}

internal enum STORAGE_QUERY_TYPE
{
    PropertyStandardQuery = 0,
    PropertyExistsQuery = 1
}

[StructLayout(LayoutKind.Sequential)]
internal struct STORAGE_PROPERTY_QUERY
{
    public int PropertyId;
    public int QueryType;
    public byte AdditionalParameters;
}

internal enum PROTOCOL_TYPE
{
    ProtocolTypeUnknown = 0,
    ProtocolTypeScsi = 1,
    ProtocolTypeAta = 2,
    ProtocolTypeNvme = 3,
    ProtocolTypeSd = 4,
    ProtocolTypeUfs = 5
}

internal enum NVME_DATA_TYPE
{
    NVMeDataTypeUnknown = 0,
    NVMeDataTypeIdentify = 1,
    NVMeDataTypeLogPage = 2,
    NVMeDataTypeFeature = 3
}

/// <summary>Parte fixa de STORAGE_DEVICE_DESCRIPTOR. As cadeias ficam no mesmo buffer, em offsets relativos ao inicio.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct STORAGE_DEVICE_DESCRIPTOR_HEADER
{
    public uint Version;
    public uint Size;
    public byte DeviceType;
    public byte DeviceTypeModifier;
    [MarshalAs(UnmanagedType.U1)]
    public bool RemovableMedia;
    [MarshalAs(UnmanagedType.U1)]
    public bool CommandQueueing;
    public uint VendorIdOffset;
    public uint ProductIdOffset;
    public uint ProductRevisionOffset;
    public uint SerialNumberOffset;
    public byte BusType;
    public uint RawPropertiesLength;
}

/// <summary>Dados textuais do disco extraidos de STORAGE_DEVICE_DESCRIPTOR.</summary>
internal sealed class StorageDeviceDescriptor
{
    public string Vendor { get; init; } = string.Empty;

    public string Product { get; init; } = string.Empty;

    public string Revision { get; init; } = string.Empty;

    public string SerialNumber { get; init; } = string.Empty;

    public byte BusType { get; init; }

    public bool RemovableMedia { get; init; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct STORAGE_PROTOCOL_SPECIFIC_DATA
{
    public int ProtocolType;
    public int DataType;
    public int ProtocolDataRequestValue;
    public int ProtocolDataRequestSubValue;
    public int ProtocolDataOffset;
    public int ProtocolDataLength;
    public int FixedProtocolReturnData;
    public int ProtocolDataRequestSubValue2;
    public int ProtocolDataRequestSubValue3;
    public int ProtocolDataRequestSubValue4;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DEVICE_SEEK_PENALTY_DESCRIPTOR
{
    public uint Version;
    public uint Size;
    [MarshalAs(UnmanagedType.U1)]
    public bool IncursSeekPenalty;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DEVICE_TRIM_DESCRIPTOR
{
    public uint Version;
    public uint Size;
    [MarshalAs(UnmanagedType.U1)]
    public bool TrimEnabled;
}

[StructLayout(LayoutKind.Sequential)]
internal struct STORAGE_ADAPTER_DESCRIPTOR
{
    public uint Version;
    public uint Size;
    public uint MaximumTransferLength;
    public uint MaximumPhysicalPages;
    public uint AlignmentMask;
    [MarshalAs(UnmanagedType.U1)]
    public bool AdapterUsesPio;
    [MarshalAs(UnmanagedType.U1)]
    public bool AdapterScansDown;
    [MarshalAs(UnmanagedType.U1)]
    public bool CommandQueueing;
    [MarshalAs(UnmanagedType.U1)]
    public bool AcceleratedTransfer;
    public byte BusType;
    public ushort BusMajorVersion;
    public ushort BusMinorVersion;
}

/// <summary>
/// Consulta de propriedades de discos via DeviceIoControl.
/// Todas as chamadas sao somente leitura e degradam para "indisponivel" quando o dispositivo nao responde.
/// </summary>
internal static class NativeStorage
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
    private const uint IOCTL_STORAGE_PREDICT_FAILURE = 0x2D1104;
    private const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;

    private const int NvmeHealthLogLength = 512;
    private const int NvmeLogPageHealthInfo = 0x02;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inputBuffer,
        uint inputBufferSize,
        IntPtr outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    internal static SafeFileHandle OpenDevice(int deviceIndex)
    {
        var path = @"\\.\PhysicalDrive" + deviceIndex;
        return CreateFile(
            path,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);
    }

    /// <summary>
    /// Le o descritor do dispositivo: fabricante, modelo, revisao de firmware, numero de serie e tipo de barramento.
    /// </summary>
    internal static StorageDeviceDescriptor? TryQueryDeviceDescriptor(SafeFileHandle device)
    {
        const int bufferSize = 4096;
        var querySize = Marshal.SizeOf<STORAGE_PROPERTY_QUERY>();
        var input = Marshal.AllocHGlobal(querySize);
        var output = Marshal.AllocHGlobal(bufferSize);

        try
        {
            Marshal.StructureToPtr(CreateQuery(STORAGE_PROPERTY_ID.StorageDeviceProperty), input, false);

            if (!DeviceIoControl(device, IOCTL_STORAGE_QUERY_PROPERTY, input, (uint)querySize, output, bufferSize, out var returned, IntPtr.Zero) || returned < 36)
            {
                return null;
            }

            var header = Marshal.PtrToStructure<STORAGE_DEVICE_DESCRIPTOR_HEADER>(output);

            string ReadString(uint offset)
            {
                if (offset == 0 || offset >= returned)
                {
                    return string.Empty;
                }

                var text = Marshal.PtrToStringAnsi(IntPtr.Add(output, (int)offset)) ?? string.Empty;
                return text.Trim();
            }

            return new StorageDeviceDescriptor
            {
                Vendor = ReadString(header.VendorIdOffset),
                Product = ReadString(header.ProductIdOffset),
                Revision = ReadString(header.ProductRevisionOffset),
                SerialNumber = ReadString(header.SerialNumberOffset),
                BusType = header.BusType,
                RemovableMedia = header.RemovableMedia
            };
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(input);
            Marshal.FreeHGlobal(output);
        }
    }

    internal static bool TryQuery<TStruct>(SafeFileHandle device, STORAGE_PROPERTY_ID property, out TStruct value)
        where TStruct : struct
    {
        value = default;

        var querySize = Marshal.SizeOf<STORAGE_PROPERTY_QUERY>();
        var outputSize = Marshal.SizeOf<TStruct>();
        var input = Marshal.AllocHGlobal(querySize);
        var output = Marshal.AllocHGlobal(outputSize);

        try
        {
            Marshal.StructureToPtr(CreateQuery(property), input, false);

            if (!DeviceIoControl(device, IOCTL_STORAGE_QUERY_PROPERTY, input, (uint)querySize, output, (uint)outputSize, out var returned, IntPtr.Zero))
            {
                return false;
            }

            if (returned < 4)
            {
                return false;
            }

            value = Marshal.PtrToStructure<TStruct>(output);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(input);
            Marshal.FreeHGlobal(output);
        }
    }

    /// <summary>
    /// Le o log de saude NVMe: dados de temperatura, horas ligado e percentual de desgaste.
    /// </summary>
    internal static byte[]? TryQueryNvmeHealthLog(SafeFileHandle device)
    {
        var querySize = Marshal.SizeOf<STORAGE_PROPERTY_QUERY>();
        var protocolSize = Marshal.SizeOf<STORAGE_PROTOCOL_SPECIFIC_DATA>();
        var inputSize = querySize + protocolSize;
        var outputSize = protocolSize + NvmeHealthLogLength;

        var input = Marshal.AllocHGlobal(inputSize);
        var output = Marshal.AllocHGlobal(outputSize);

        try
        {
            var protocol = new STORAGE_PROTOCOL_SPECIFIC_DATA
            {
                ProtocolType = (int)PROTOCOL_TYPE.ProtocolTypeNvme,
                DataType = (int)NVME_DATA_TYPE.NVMeDataTypeLogPage,
                ProtocolDataRequestValue = NvmeLogPageHealthInfo,
                ProtocolDataRequestSubValue = 0,
                ProtocolDataOffset = protocolSize,
                ProtocolDataLength = NvmeHealthLogLength
            };

            Marshal.StructureToPtr(CreateQuery(STORAGE_PROPERTY_ID.StorageDeviceProtocolSpecificProperty), input, false);
            Marshal.StructureToPtr(protocol, IntPtr.Add(input, querySize), false);

            if (!DeviceIoControl(device, IOCTL_STORAGE_QUERY_PROPERTY, input, (uint)inputSize, output, (uint)outputSize, out var returned, IntPtr.Zero) || returned == 0)
            {
                return null;
            }

            var header = Marshal.PtrToStructure<STORAGE_PROTOCOL_SPECIFIC_DATA>(output);
            var offset = header.ProtocolDataOffset >= protocolSize && header.ProtocolDataOffset + NvmeHealthLogLength <= returned
                ? header.ProtocolDataOffset
                : 0;

            if (returned < offset + NvmeHealthLogLength)
            {
                return null;
            }

            var payload = new byte[NvmeHealthLogLength];
            Marshal.Copy(IntPtr.Add(output, offset), payload, 0, NvmeHealthLogLength);
            return payload;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(input);
            Marshal.FreeHGlobal(output);
        }
    }

    /// <summary>
    /// Capacidade total do disco fisico em bytes (GET_LENGTH_INFO).
    /// </summary>
    internal static long TryQueryDiskLength(SafeFileHandle device)
    {
        const int outputSize = 8;
        var output = Marshal.AllocHGlobal(outputSize);

        try
        {
            if (!DeviceIoControl(device, IOCTL_DISK_GET_LENGTH_INFO, IntPtr.Zero, 0, output, outputSize, out var returned, IntPtr.Zero) || returned < outputSize)
            {
                return 0;
            }

            return Marshal.ReadInt64(output);
        }
        catch (Exception)
        {
            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(output);
        }
    }

    /// <summary>
    /// Consulta de falha prevista (SMART). Quando o dispositivo nao responde, o resultado e "indisponivel".
    /// </summary>
    internal static bool TryQueryPredictFailure(SafeFileHandle device, out bool predictsFailure)
    {
        predictsFailure = false;

        // STORAGE_PREDICT_FAILURE: ULONG PredictFailure seguido de dados do fornecedor.
        const int outputSize = 516;
        var output = Marshal.AllocHGlobal(outputSize);

        try
        {
            if (!DeviceIoControl(device, IOCTL_STORAGE_PREDICT_FAILURE, IntPtr.Zero, 0, output, outputSize, out var returned, IntPtr.Zero) || returned < 4)
            {
                return false;
            }

            predictsFailure = Marshal.ReadInt32(output) != 0;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(output);
        }
    }

    private static STORAGE_PROPERTY_QUERY CreateQuery(STORAGE_PROPERTY_ID property) => new()
    {
        PropertyId = (int)property,
        QueryType = (int)STORAGE_QUERY_TYPE.PropertyStandardQuery
    };
}
