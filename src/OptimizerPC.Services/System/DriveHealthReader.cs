using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Services.Interop;

namespace OptimizerPC.Services.System;

/// <summary>Saude de um disco fisico, incluindo a origem dos dados informada.</summary>
internal sealed class DiskHealth
{
    public required StorageDeviceReader.PhysicalDisk Disk { get; init; }

    public StorageMediaType MediaType { get; init; } = StorageMediaType.Unknown;

    public DriveHealthStatus Status { get; init; } = DriveHealthStatus.Unknown;

    public bool SmartAvailable { get; init; }

    public int? TemperatureCelsius { get; init; }

    public long? PowerOnHours { get; init; }

    public long? TotalBytesWritten { get; init; }

    public int? WearPercent { get; init; }

    /// <summary>Chave de localizacao que explica de onde vieram os dados de saude.</summary>
    public string SourceKey { get; init; } = "Health.Source.NotAvailable";

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Leitura de saude dos discos fisicos. Usa a previsao de falha (SMART) exposta pelo
/// Windows e, em unidades NVMe, o log de saude do proprio dispositivo. Nenhuma escrita,
/// nenhum comando de firmware e nenhuma alteracao no disco sao realizados: quando o
/// dispositivo nao expoe os dados pela interface disponivel, o resultado e
/// "informacao nao disponivel" em vez de um valor estimado.
/// </summary>
internal static class DriveHealthReader
{
    private static readonly string[] RequiredAccessNotes = { "Health.Note.RequiresElevation" };

    internal static IReadOnlyList<DiskHealth> ReadAll()
    {
        var disks = StorageDeviceReader.ReadAll();
        if (disks.Count == 0)
        {
            return Array.Empty<DiskHealth>();
        }

        var elevated = NativeProcess.IsCurrentProcessElevated();
        var results = new List<DiskHealth>(disks.Count);

        foreach (var disk in disks)
        {
            if (elevated is false)
            {
                results.Add(Unavailable(disk, "Health.Source.RequiresElevation", RequiredAccessNotes, MediaTypeOf(disk)));
                continue;
            }

            results.Add(Read(disk));
        }

        return results;
    }

    private static DiskHealth Read(StorageDeviceReader.PhysicalDisk disk)
    {
        var notes = new List<string>();
        var mediaType = MediaTypeOf(disk);

        using var device = NativeStorage.OpenDevice(disk.Index);
        if (device.IsInvalid)
        {
            return Unavailable(disk, "Health.Source.NotAvailable", RequiredAccessNotes, mediaType);
        }

        var predictFailureKnown = NativeStorage.TryQueryPredictFailure(device, out var predictsFailure);
        var smartAvailable = predictFailureKnown;
        var status = predictFailureKnown
            ? (predictsFailure ? DriveHealthStatus.Failing : DriveHealthStatus.Healthy)
            : DriveHealthStatus.Unavailable;
        var sourceKey = predictFailureKnown ? "Health.Source.Smart" : "Health.Source.NotAvailable";

        if (predictsFailure)
        {
            notes.Add("Health.Note.PredictFailure");
        }

        int? temperature = null;
        int? wear = null;
        long? powerOnHours = null;
        long? bytesWritten = null;

        var healthLog = NativeStorage.TryQueryNvmeHealthLog(device);
        if (healthLog is { Length: >= 176 })
        {
            var criticalWarning = ReadUInt16(healthLog, 0);
            var compositeTemperature = ReadUInt16(healthLog, 2);
            var availableSpare = healthLog[4];
            var spareThreshold = healthLog[5];
            var percentageUsed = healthLog[6];
            wear = percentageUsed;
            powerOnHours = ToHours(ReadUInt64(healthLog, 128));
            bytesWritten = Scale(ReadUInt64(healthLog, 48));

            temperature = ToCelsius(compositeTemperature);
            smartAvailable = true;
            sourceKey = "Health.Source.Nvme";
            status = EvaluateNvme(criticalWarning, availableSpare, spareThreshold, percentageUsed, notes);
        }
        else if (predictFailureKnown is false)
        {
            notes.Add("Health.Note.SmartUnavailable");
        }

        if (mediaType is StorageMediaType.Hdd && healthLog is null)
        {
            notes.Add("Health.Note.AtaSmartDetailsUnavailable");
        }

        return new DiskHealth
        {
            Disk = disk,
            MediaType = mediaType,
            Status = status,
            SmartAvailable = smartAvailable,
            TemperatureCelsius = temperature,
            PowerOnHours = powerOnHours,
            TotalBytesWritten = bytesWritten,
            WearPercent = wear,
            SourceKey = sourceKey,
            Notes = notes
        };
    }

    private static DiskHealth Unavailable(
        StorageDeviceReader.PhysicalDisk disk,
        string sourceKey,
        IReadOnlyList<string> notes,
        StorageMediaType mediaType) => new()
    {
        Disk = disk,
        MediaType = mediaType,
        Status = DriveHealthStatus.Unavailable,
        SmartAvailable = false,
        SourceKey = sourceKey,
        Notes = notes
    };

    private static StorageMediaType MediaTypeOf(StorageDeviceReader.PhysicalDisk disk)
    {
        if (disk.SeekPenaltyKnown)
        {
            return disk.SeekPenalty ? StorageMediaType.Hdd : StorageMediaType.Ssd;
        }

        return disk.BusType == (byte)StorageBusType.Nvme ? StorageMediaType.ScmOrNvme : StorageMediaType.Unknown;
    }

    private static DriveHealthStatus EvaluateNvme(int criticalWarning, int availableSpare, int spareThreshold, int wear, List<string> notes)
    {
        if (criticalWarning != 0)
        {
            notes.Add("Health.Note.CriticalWarning");
            return DriveHealthStatus.Failing;
        }

        if (wear >= 90)
        {
            notes.Add("Health.Note.WearHigh");
            return DriveHealthStatus.Warning;
        }

        if (spareThreshold > 0 && availableSpare <= spareThreshold)
        {
            notes.Add("Health.Note.SpareLow");
            return DriveHealthStatus.Warning;
        }

        return DriveHealthStatus.Healthy;
    }

    private static ushort ReadUInt16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static ulong ReadUInt64(byte[] data, int offset)
    {
        ulong value = 0;
        for (var index = 7; index >= 0; index--)
        {
            value = (value << 8) | data[offset + index];
        }

        return value;
    }

    /// <summary>As unidades de dados do NVMe sao contadas em blocos de 512.000 bytes.</summary>
    private static long Scale(ulong dataUnits)
    {
        try
        {
            return checked((long)(dataUnits * 512_000UL));
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private static long ToHours(ulong hours) => hours > long.MaxValue ? long.MaxValue : (long)hours;

    private static int? ToCelsius(ushort kelvin) =>
        kelvin is > 0 and < 400 ? kelvin - 273 : null;
}
