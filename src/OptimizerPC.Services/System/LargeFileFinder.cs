using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.System;

/// <summary>
/// Localizacao de arquivos grandes. A varredura e somente leitura: os arquivos sao
/// apenas listados para decisao do usuario, nunca apagados ou movidos automaticamente.
/// </summary>
public sealed class LargeFileFinder : ILargeFileFinder
{
    private const int MaxResults = 500;
    private const int MaxCandidates = 5000;
    private const int ProgressInterval = 400;

    private static readonly EnumerationOptions ScanOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false,
        MatchType = MatchType.Simple
    };

    private readonly IAppLogger _logger;

    public LargeFileFinder(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<LargeFileInfo>> FindAsync(
        IReadOnlyList<string> roots,
        long minimumSize,
        IProgress<StorageScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Find(roots, minimumSize, progress, cancellationToken), cancellationToken);

    private IReadOnlyList<LargeFileInfo> Find(
        IReadOnlyList<string> roots,
        long minimumSize,
        IProgress<StorageScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var candidates = new List<LargeFileInfo>();
        var pending = new Queue<string>();
        var filesProcessed = 0;
        var bytesProcessed = 0L;
        var lastReported = 0;
        var limit = UsedBytesOf(roots);

        foreach (var root in roots)
        {
            if (Directory.Exists(root))
            {
                pending.Enqueue(root);
            }
        }

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
                    filesProcessed++;

                    long length;
                    DateTime lastWrite;
                    try
                    {
                        length = file.Length;
                        lastWrite = file.LastWriteTimeUtc;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    bytesProcessed += length;

                    if (length < minimumSize || candidates.Count >= MaxCandidates)
                    {
                        continue;
                    }

                    candidates.Add(new LargeFileInfo
                    {
                        FullPath = file.FullName,
                        FileName = file.Name,
                        Folder = directory,
                        Extension = file.Extension,
                        SizeBytes = length,
                        LastWriteTimeUtc = lastWrite
                    });
                }

                foreach (var child in info.EnumerateDirectories("*", ScanOptions))
                {
                    pending.Enqueue(child.FullName);
                }
            }
            catch (Exception exception)
            {
                _logger.Debug("Storage", "Pasta ignorada na busca de arquivos grandes: " + directory + " (" + exception.GetType().Name + ").");
                continue;
            }

            if (progress is not null && filesProcessed - lastReported >= ProgressInterval)
            {
                lastReported = filesProcessed;
                progress.Report(new StorageScanProgress(directory, filesProcessed, bytesProcessed, Percent(bytesProcessed, limit)));
            }
        }

        progress?.Report(new StorageScanProgress(string.Empty, filesProcessed, bytesProcessed, 100));

        return candidates
            .OrderByDescending(f => f.SizeBytes)
            .Take(MaxResults)
            .ToArray();
    }

    private static long UsedBytesOf(IReadOnlyList<string> roots)
    {
        var volumes = VolumeEnumerator.GetVolumes();
        var total = 0L;

        foreach (var root in roots)
        {
            string? volumeRoot;
            try
            {
                volumeRoot = Path.GetPathRoot(root);
            }
            catch (Exception)
            {
                continue;
            }

            var volume = volumes.FirstOrDefault(v => string.Equals(v.RootPath, volumeRoot, StringComparison.OrdinalIgnoreCase));
            if (volume is not null)
            {
                total += volume.UsedBytes;
            }
        }

        return total;
    }

    private static double Percent(long processed, long limit) =>
        limit <= 0 ? 0 : Math.Clamp(processed * 100.0 / limit, 0, 99.9);
}
