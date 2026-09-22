using System.Globalization;
using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.Core.Formatting;

/// <summary>
/// Conversoes de exibicao. Mantidas em um unico ponto para que a interface,
/// os relatorios e os logs apresentem os mesmos numeros da mesma forma.
/// </summary>
public static class Humanize
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    /// <summary>Converte bytes em texto legivel (ex.: "1,24 GB").</summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 0)
        {
            return "0 B";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit switch
        {
            0 => string.Create(CultureInfo.CurrentCulture, $"{value:0} {Units[unit]}"),
            1 => string.Create(CultureInfo.CurrentCulture, $"{value:0} {Units[unit]}"),
            _ => string.Create(CultureInfo.CurrentCulture, $"{value:0.##} {Units[unit]}")
        };
    }

    /// <summary>Converte bytes em texto legivel usando uma cultura especifica.</summary>
    public static string Bytes(long bytes, CultureInfo culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            return Bytes(bytes);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    public static string Percent(double value, int decimals = 0)
    {
        var format = decimals <= 0 ? "0" : "0." + new string('#', decimals);
        return value.ToString(format, CultureInfo.CurrentCulture) + "%";
    }

    public static string Speed(double bytesPerSecond) => $"{Bytes((long)Math.Max(0, bytesPerSecond))}/s";

    /// <summary>
    /// Duracao curta e legivel. O texto abaixo de um segundo vem do chamador, que
    /// e quem tem acesso ao catalogo de idioma.
    /// </summary>
    public static string Duration(TimeSpan span, string subSecondText)
    {
        if (span.TotalSeconds < 1)
        {
            return subSecondText;
        }

        if (span.TotalMinutes < 1)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{span.TotalSeconds:0} s");
        }

        if (span.TotalHours < 1)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{span.Minutes} min {span.Seconds} s");
        }

        if (span.TotalDays < 1)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{span.Hours} h {span.Minutes} min");
        }

        return string.Create(CultureInfo.CurrentCulture, $"{(int)span.TotalDays} d {span.Hours} h");
    }

    /// <summary>Formata um tempo ligado (uptime) em dias, horas e minutos.</summary>
    public static string Uptime(TimeSpan span, string subSecondText)
    {
        if (span.TotalDays >= 1)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{(int)span.TotalDays} d {span.Hours} h {span.Minutes} min");
        }

        return Duration(span, subSecondText);
    }

    /// <summary>Data e hora no formato curto da cultura em uso.</summary>
    public static string Date(DateTime value) =>
        value.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern + " HH:mm", CultureInfo.CurrentCulture);

    /// <summary>Descricao tecnica do tipo de midia. Sem traducao: siglas de mercado.</summary>
    public static string MediaType(StorageMediaType type) => type switch
    {
        StorageMediaType.Ssd => "SSD",
        StorageMediaType.Hdd => "HDD",
        StorageMediaType.ScmOrNvme => "NVMe",
        _ => type.ToString()
    };

    /// <summary>Tipo de midia com nome traduzido quando a sigla nao for autoexplicativa.</summary>
    public static string MediaType(StorageMediaType type, ILocalizer localizer) =>
        type == StorageMediaType.Unknown ? localizer["Common.NotIdentified"] : MediaType(type);

    /// <summary>Descricao tecnica do barramento. Sem traducao: siglas de mercado.</summary>
    public static string BusType(StorageBusType type) => type switch
    {
        StorageBusType.Nvme => "NVMe",
        StorageBusType.Sata => "SATA",
        StorageBusType.Ata => "ATA",
        StorageBusType.Atapi => "ATAPI",
        StorageBusType.Usb => "USB",
        StorageBusType.Raid => "RAID",
        StorageBusType.Sas => "SAS",
        StorageBusType.Scsi => "SCSI",
        StorageBusType.Virtual => "Virtual",
        StorageBusType.Spaces => "Storage Spaces",
        StorageBusType.Mmc => "MMC",
        StorageBusType.Sd => "SD",
        StorageBusType.IScsi => "iSCSI",
        StorageBusType.Ieee1394 => "IEEE 1394",
        _ => type.ToString()
    };

    /// <summary>Barramento com nome traduzido para os cartoes removiveis.</summary>
    public static string BusType(StorageBusType type, ILocalizer localizer) => type switch
    {
        StorageBusType.Mmc => localizer["Storage.Bus.MemoryCard"],
        StorageBusType.Sd => localizer["Storage.Bus.SdCard"],
        StorageBusType.Unknown => localizer["Common.NotIdentified"],
        _ => BusType(type)
    };

    /// <summary>Nucleos e threads do processador, traduzido pelo chamador.</summary>
    public static string Cores(int physicalCores, int logicalProcessors, ILocalizer localizer) =>
        physicalCores > 0 && logicalProcessors > physicalCores
            ? localizer.Format("Common.Cpu.CoresThreads", physicalCores, logicalProcessors)
            : localizer.Format("Common.Cpu.Cores", Math.Max(physicalCores, logicalProcessors));

    /// <summary>Classificacao do volume (disco fixo, removivel, rede, optico).</summary>
    public static string DriveKind(VolumeDriveKind kind, ILocalizer localizer) =>
        localizer["Common.DriveKind." + kind];

    /// <summary>Codigo do idioma no formato usado pelo atributo lang de documentos exportados.</summary>
    public static string LanguageCode(AppLanguage language) => language switch
    {
        AppLanguage.EnUs => "en-US",
        AppLanguage.Es => "es",
        _ => "pt-BR"
    };
}
