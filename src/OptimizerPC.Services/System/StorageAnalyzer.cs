using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Interop;

namespace OptimizerPC.Services.System;

/// <summary>
/// Analise de uso do disco. Percorre as pastas em largura, classifica cada arquivo por
/// pasta conhecida e soma os bytes encontrados. Tudo em modo somente leitura: nenhum
/// arquivo e movido, renomeado ou apagado durante a analise.
/// </summary>
public sealed class StorageAnalyzer : IStorageAnalyzer
{
    private const int ProgressInterval = 400;

    private static readonly EnumerationOptions ScanOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false,
        MatchType = MatchType.Simple
    };

    private static readonly string[] SystemRootFiles =
    {
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log.tmp"
    };

    private static readonly string[] UserFolders =
    {
        "Documents", "Downloads", "Pictures", "Videos", "Music", "Desktop"
    };

    private readonly IAppLogger _logger;

    public StorageAnalyzer(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<StorageAnalysisResult> AnalyzeAsync(IProgress<StorageScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Analyze(progress, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<VolumeInfo>> GetVolumesAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<VolumeInfo>>(VolumeEnumerator.GetVolumes, cancellationToken);

    public Task<IReadOnlyList<StorageDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<StorageDeviceInfo>>(ReadDevices, cancellationToken);

    private static IReadOnlyList<StorageDeviceInfo> ReadDevices()
    {
        if (NativeProcess.IsCurrentProcessElevated() is false)
        {
            // A leitura dos discos fisicos exige acesso ao dispositivo, concedido apenas a administradores.
            return Array.Empty<StorageDeviceInfo>();
        }

        return StorageDeviceReader.ReadAll()
            .Select(disk => new StorageDeviceInfo
            {
                DeviceIndex = disk.Index,
                Model = disk.Model,
                SerialNumber = disk.SerialNumber,
                FirmwareRevision = disk.FirmwareRevision,
                SizeBytes = disk.SizeBytes,
                BusType = (StorageBusType)disk.BusType,
                MediaType = MediaTypeOf(disk)
            })
            .ToArray();
    }

    private static StorageMediaType MediaTypeOf(StorageDeviceReader.PhysicalDisk disk)
    {
        if (disk.SeekPenaltyKnown)
        {
            return disk.SeekPenalty ? StorageMediaType.Hdd : StorageMediaType.Ssd;
        }

        return disk.BusType == (byte)StorageBusType.Nvme ? StorageMediaType.ScmOrNvme : StorageMediaType.Unknown;
    }

    private StorageAnalysisResult Analyze(IProgress<StorageScanProgress>? progress, CancellationToken cancellationToken)
    {
        var root = VolumeEnumerator.GetSystemDriveRoot();
        var volume = VolumeEnumerator.GetVolumes()
            .FirstOrDefault(v => string.Equals(v.RootPath, root, StringComparison.OrdinalIgnoreCase));

        var usages = new Dictionary<StorageCategory, StorageCategoryUsage>();
        var pending = new Queue<string>();
        var filesProcessed = 0;
        var bytesProcessed = 0L;
        var temporaryBytes = 0L;
        var lastReported = 0;
        var limit = Math.Max(1, volume?.UsedBytes ?? 0);

        progress?.Report(new StorageScanProgress(root, 0, 0, 0));
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Dequeue();

            DirectoryInfo info;
            try
            {
                info = new DirectoryInfo(directory);
                if (info.Exists is false)
                {
                    continue;
                }
            }
            catch (Exception)
            {
                continue;
            }

            try
            {
                foreach (var file in info.EnumerateFiles("*", ScanOptions))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    long length;
                    try
                    {
                        length = file.Length;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    var category = ClassifyFile(root, file.FullName);
                    var usage = GetOrAdd(usages, category);
                    usage.Bytes += length;
                    usage.FileCount++;
                    filesProcessed++;
                    bytesProcessed += length;

                    if (category is StorageCategory.Temporary)
                    {
                        temporaryBytes += length;
                    }
                }

                foreach (var child in info.EnumerateDirectories("*", ScanOptions))
                {
                    pending.Enqueue(child.FullName);
                }
            }
            catch (Exception exception)
            {
                _logger.Debug("Storage", "Pasta ignorada na analise: " + directory + " (" + exception.GetType().Name + ").");
                continue;
            }

            if (progress is not null && filesProcessed - lastReported >= ProgressInterval)
            {
                lastReported = filesProcessed;
                progress.Report(new StorageScanProgress(directory, filesProcessed, bytesProcessed, Percent(bytesProcessed, limit)));
            }
        }

        progress?.Report(new StorageScanProgress(root, filesProcessed, bytesProcessed, 100));

        _logger.Info("Storage", "Analise concluida em " + root + ": " + filesProcessed + " arquivos, " + bytesProcessed + " bytes.");

        return new StorageAnalysisResult
        {
            RootPath = root,
            TotalBytes = volume?.TotalBytes ?? 0,
            FreeBytes = volume?.FreeBytes ?? 0,
            TemporaryBytes = temporaryBytes,
            Categories = usages.Values.OrderBy(u => u.Category).ToArray(),
            Notes = new[] { "Storage.Note.HardLinksCounted" }
        };
    }

    private static double Percent(long processed, long limit) =>
        limit <= 0 ? 0 : Math.Clamp(processed * 100.0 / limit, 0, 99.9);

    private static StorageCategoryUsage GetOrAdd(Dictionary<StorageCategory, StorageCategoryUsage> usages, StorageCategory category)
    {
        if (usages.TryGetValue(category, out var existing))
        {
            return existing;
        }

        var created = new StorageCategoryUsage { Category = category };
        usages[category] = created;
        return created;
    }

    /// <summary>Classifica um arquivo pela primeira pasta conhecida do caminho relativo.</summary>
    internal static StorageCategory ClassifyFile(string root, string fullPath)
    {
        string relative;
        try
        {
            relative = Path.GetRelativePath(root, fullPath);
        }
        catch (Exception)
        {
            return StorageCategory.Other;
        }

        if (relative.Length == 0 || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return StorageCategory.Other;
        }

        var segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return StorageCategory.Other;
        }

        if (segments.Length == 1)
        {
            return SystemRootFiles.Contains(segments[0], StringComparer.OrdinalIgnoreCase)
                ? StorageCategory.System
                : StorageCategory.Other;
        }

        return segments[0].ToLowerInvariant() switch
        {
            "windows" => HasTemporarySegment(segments) ? StorageCategory.Temporary : StorageCategory.System,
            "programdata" => StorageCategory.System,
            "$recycle.bin" => StorageCategory.System,
            "system volume information" => StorageCategory.System,
            "recovery" => StorageCategory.System,
            "boot" => StorageCategory.System,
            "program files" => StorageCategory.Applications,
            "program files (x86)" => StorageCategory.Applications,
            "users" => ClassifyUserPath(segments),
            _ => HasTemporarySegment(segments) ? StorageCategory.Temporary : StorageCategory.Other
        };
    }

    private static StorageCategory ClassifyUserPath(string[] segments)
    {
        if (HasTemporarySegment(segments))
        {
            return StorageCategory.Temporary;
        }

        // Estrutura esperada: Users\<nome da conta>\<pasta>.
        if (segments.Length < 3)
        {
            return StorageCategory.Other;
        }

        var folder = segments[2];
        var match = UserFolders.FirstOrDefault(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return StorageCategory.Other;
        }

        return match switch
        {
            "Documents" => StorageCategory.Documents,
            "Downloads" => StorageCategory.Downloads,
            "Pictures" => StorageCategory.Pictures,
            "Videos" => StorageCategory.Videos,
            "Music" => StorageCategory.Music,
            _ => StorageCategory.Desktop
        };
    }

    private static bool HasTemporarySegment(string[] segments)
    {
        foreach (var segment in segments)
        {
            if (string.Equals(segment, "Temp", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
