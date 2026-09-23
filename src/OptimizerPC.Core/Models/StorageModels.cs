using OptimizerPC.Core.Formatting;

namespace OptimizerPC.Core.Models;

public sealed class VolumeInfo
{
    public string DriveLetter { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string FileSystem { get; init; } = string.Empty;
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public VolumeDriveKind DriveKind { get; init; }
    public bool IsSystemDrive { get; init; }
    public bool IsReady { get; init; }

    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    public double UsedPercent => TotalBytes <= 0 ? 0 : UsedBytes * 100.0 / TotalBytes;

    public double FreePercent => TotalBytes <= 0 ? 0 : FreeBytes * 100.0 / TotalBytes;

    public string TotalText => Humanize.Bytes(TotalBytes);

    public string FreeText => Humanize.Bytes(FreeBytes);

    public string UsedText => Humanize.Bytes(UsedBytes);
}

public sealed class StorageDeviceInfo
{
    public int DeviceIndex { get; init; }
    public string Model { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public string FirmwareRevision { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public StorageBusType BusType { get; init; } = StorageBusType.Unknown;
    public StorageMediaType MediaType { get; init; } = StorageMediaType.Unknown;
    public int? RotationRateRpm { get; init; }
    public DriveHealthStatus Health { get; init; } = DriveHealthStatus.Unknown;
    public int? TemperatureCelsius { get; init; }
    public bool SmartSupported { get; init; }
    public int? PowerOnHours { get; init; }
    public string HealthDetail { get; init; } = string.Empty;

    public string SizeText => Humanize.Bytes(SizeBytes);
}

public sealed class StorageCategoryUsage
{
    public StorageCategory Category { get; init; }
    public long Bytes { get; set; }
    public long FileCount { get; set; }

    public string BytesText => Humanize.Bytes(Bytes);
}

public sealed class StorageAnalysisResult
{
    public string RootPath { get; init; } = string.Empty;
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public long TemporaryBytes { get; init; }
    public IReadOnlyList<StorageCategoryUsage> Categories { get; init; } = Array.Empty<StorageCategoryUsage>();
    public DateTime CompletedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Observacoes honestas sobre limites da leitura (chaves de localizacao).</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
}

public sealed class LargeFileInfo
{
    public string FullPath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Folder { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }

    public string SizeText => Humanize.Bytes(SizeBytes);

    public string LastWriteText => Humanize.Date(LastWriteTimeUtc.ToLocalTime());
}

public sealed class DuplicateFileInfo
{
    public string FullPath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
    public string Hash { get; init; } = string.Empty;

    public string SizeText => Humanize.Bytes(SizeBytes);

    public string LastWriteText => Humanize.Date(LastWriteTimeUtc.ToLocalTime());
}

public sealed class DuplicateGroup
{
    public string Hash { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public IReadOnlyList<DuplicateFileInfo> Files { get; init; } = Array.Empty<DuplicateFileInfo>();

    /// <summary>Espaco ocupado por copias extras do mesmo arquivo.</summary>
    public long WastedBytes => FileSizeBytes * Math.Max(0, Files.Count - 1);

    public string WastedText => Humanize.Bytes(WastedBytes);

    public string FileSizeText => Humanize.Bytes(FileSizeBytes);
}

public sealed class DuplicateScanResult
{
    public IReadOnlyList<DuplicateGroup> Groups { get; init; } = Array.Empty<DuplicateGroup>();
    public int ScannedFiles { get; init; }
    public long ScannedBytes { get; init; }
    public bool WasCancelled { get; init; }
    public TimeSpan Duration { get; init; }

    public long TotalWastedBytes => Groups.Sum(g => g.WastedBytes);

    public int TotalDuplicateFiles => Groups.Sum(g => g.Files.Count);
}

/// <summary>Progresso da busca de arquivos duplicados.</summary>
public readonly record struct DuplicateScanProgress(
    int FilesHashed,
    int GroupsFound,
    string CurrentPath);

/// <summary>
/// Resultado do envio de um arquivo para a Lixeira.
/// <see cref="WasBlocked"/> indica que o proprio aplicativo recusou a operacao;
/// quando falso e <see cref="Moved"/> tambem e falso, o Windows recusou.
/// </summary>
public sealed class FileMoveOutcome
{
    public string Path { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public bool Moved { get; init; }
    public bool WasBlocked { get; init; }
    public string ReasonKey { get; init; } = string.Empty;

    public string FileName
    {
        get
        {
            try
            {
                return System.IO.Path.GetFileName(Path);
            }
            catch (Exception)
            {
                return Path;
            }
        }
    }
}

/// <summary>Resultado consolidado do envio de arquivos para a Lixeira.</summary>
public sealed class FileMoveResult
{
    public IReadOnlyList<FileMoveOutcome> Outcomes { get; init; } = Array.Empty<FileMoveOutcome>();

    public bool WasCancelled { get; init; }

    public IReadOnlyList<FileMoveOutcome> MovedFiles { get; init; } = Array.Empty<FileMoveOutcome>();

    public IReadOnlyList<FileMoveOutcome> BlockedFiles { get; init; } = Array.Empty<FileMoveOutcome>();

    public IReadOnlyList<FileMoveOutcome> FailedFiles { get; init; } = Array.Empty<FileMoveOutcome>();

    public long MovedBytes { get; init; }

    public int MovedCount => MovedFiles.Count;

    public int BlockedCount => BlockedFiles.Count;

    public int FailedCount => FailedFiles.Count;

    public bool HasAnyMoved => MovedCount > 0;

    public bool HasAnyBlocked => BlockedCount > 0;

    public bool HasAnyFailed => FailedCount > 0;
}

public sealed class InstalledGame
{
    public string Name { get; init; } = string.Empty;
    public string InstallPath { get; init; } = string.Empty;
    public string? ExecutablePath { get; init; }
    public long SizeBytes { get; init; }
    public GameLauncherKind Launcher { get; init; } = GameLauncherKind.Unknown;
}
