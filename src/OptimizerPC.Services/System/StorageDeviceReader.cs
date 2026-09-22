using OptimizerPC.Services.Interop;

namespace OptimizerPC.Services.System;

/// <summary>
/// Leitura dos discos fisicos por DeviceIoControl. Nenhuma operacao de escrita,
/// particionamento ou formatacao e executada: apenas consulta de propriedades.
/// </summary>
internal static class StorageDeviceReader
{
    private const int MaxDevices = 32;

    internal sealed class PhysicalDisk
    {
        public int Index { get; init; }

        public string Model { get; init; } = string.Empty;

        public string SerialNumber { get; init; } = string.Empty;

        public string FirmwareRevision { get; init; } = string.Empty;

        public long SizeBytes { get; init; }

        public byte BusType { get; init; }

        public bool Removable { get; init; }

        public bool SeekPenalty { get; init; }

        /// <summary>Verdadeiro quando o dispositivo informou a presenca (ou ausencia) de penalidade de busca.</summary>
        public bool SeekPenaltyKnown { get; init; }

        public bool TrimEnabled { get; init; }
    }

    internal static IReadOnlyList<PhysicalDisk> ReadAll()
    {
        var disks = new List<PhysicalDisk>();
        var consecutiveMisses = 0;

        for (var index = 0; index < MaxDevices && consecutiveMisses < 3; index++)
        {
            var disk = TryRead(index);
            if (disk is null)
            {
                consecutiveMisses++;
                continue;
            }

            consecutiveMisses = 0;
            disks.Add(disk);
        }

        return disks;
    }

    internal static PhysicalDisk? TryRead(int index)
    {
        try
        {
            using var device = NativeStorage.OpenDevice(index);
            if (device.IsInvalid)
            {
                return null;
            }

            var descriptor = NativeStorage.TryQueryDeviceDescriptor(device);
            if (descriptor is null)
            {
                return null;
            }

            NativeStorage.TryQuery(device, STORAGE_PROPERTY_ID.StorageDeviceSeekPenaltyProperty, out DEVICE_SEEK_PENALTY_DESCRIPTOR seek);
            NativeStorage.TryQuery(device, STORAGE_PROPERTY_ID.StorageDeviceTrimProperty, out DEVICE_TRIM_DESCRIPTOR trim);

            return new PhysicalDisk
            {
                Index = index,
                Model = JoinModel(descriptor.Vendor, descriptor.Product),
                SerialNumber = descriptor.SerialNumber,
                FirmwareRevision = descriptor.Revision,
                SizeBytes = NativeStorage.TryQueryDiskLength(device),
                BusType = descriptor.BusType,
                Removable = descriptor.RemovableMedia,
                SeekPenalty = seek.IncursSeekPenalty,
                SeekPenaltyKnown = seek.Version != 0,
                TrimEnabled = trim.TrimEnabled
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string JoinModel(string vendor, string product)
    {
        if (string.IsNullOrWhiteSpace(vendor))
        {
            return product;
        }

        if (string.IsNullOrWhiteSpace(product))
        {
            return vendor;
        }

        return vendor + " " + product;
    }
}
