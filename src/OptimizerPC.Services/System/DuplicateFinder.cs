using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.System;

/// <summary>
/// Localizacao de arquivos duplicados por conteudo. A busca tem tres etapas para evitar
/// leitura desnecessaria: agrupamento por tamanho, comparacao dos primeiros 64 KB e,
/// somente quando necessario, o calculo do hash completo. Nada e apagado: o resultado
/// serve de apoio a decisao do usuario.
/// </summary>
public sealed class DuplicateFinder : IDuplicateFinder
{
    private const int QuickHashBytes = 64 * 1024;
    private const int BufferSize = 128 * 1024;
    private const int MaxGroups = 500;
    private const int MaxFilesPerGroup = 64;

    private static readonly EnumerationOptions ScanOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false,
        MatchType = MatchType.Simple
    };

    private readonly IAppLogger _logger;

    public DuplicateFinder(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<DuplicateScanResult> FindAsync(
        IReadOnlyList<string> roots,
        long minimumSize,
        IProgress<DuplicateScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Find(roots, minimumSize, progress, cancellationToken), cancellationToken);

    private DuplicateScanResult Find(
        IReadOnlyList<string> roots,
        long minimumSize,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var filesHashed = 0;
        var groupsFound = 0;
        var scannedFiles = 0;
        var scannedBytes = 0L;
        var wasCancelled = false;
        var groups = new List<DuplicateGroup>();

        try
        {
            var bySize = CollectBySize(roots, minimumSize, ref scannedFiles, ref scannedBytes, cancellationToken);

            var byQuickHash = new Dictionary<(long Size, string Hash), List<FileEntry>>();
            foreach (var group in bySize.Values)
            {
                if (group.Count < 2)
                {
                    continue;
                }

                foreach (var entry in group)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var quickHash = TryQuickHash(entry);
                    if (quickHash is null)
                    {
                        continue;
                    }

                    filesHashed++;
                    var key = (entry.SizeBytes, quickHash);
                    if (byQuickHash.TryGetValue(key, out var bucket) is false)
                    {
                        bucket = new List<FileEntry>();
                        byQuickHash[key] = bucket;
                    }

                    bucket.Add(entry);
                    progress?.Report(new DuplicateScanProgress(filesHashed, groupsFound, entry.FullPath));
                }
            }

            foreach (var candidate in byQuickHash.Values)
            {
                if (candidate.Count < 2)
                {
                    continue;
                }

                var byFullHash = new Dictionary<string, List<FileEntry>>(StringComparer.Ordinal);
                foreach (var entry in candidate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fullHash = TryFullHash(entry, cancellationToken);
                    if (fullHash is null)
                    {
                        continue;
                    }

                    if (byFullHash.TryGetValue(fullHash, out var bucket) is false)
                    {
                        bucket = new List<FileEntry>();
                        byFullHash[fullHash] = bucket;
                    }

                    bucket.Add(entry);
                    progress?.Report(new DuplicateScanProgress(filesHashed, groupsFound, entry.FullPath));
                }

                foreach (var duplicate in byFullHash)
                {
                    if (duplicate.Value.Count < 2)
                    {
                        continue;
                    }

                    groups.Add(new DuplicateGroup
                    {
                        Hash = duplicate.Key,
                        Extension = Path.GetExtension(duplicate.Value[0].FullPath),
                        FileSizeBytes = duplicate.Value[0].SizeBytes,
                        Files = duplicate.Value
                            .OrderBy(f => f.LastWriteTimeUtc)
                            .Select(f => new DuplicateFileInfo
                            {
                                FullPath = f.FullPath,
                                SizeBytes = f.SizeBytes,
                                LastWriteTimeUtc = f.LastWriteTimeUtc,
                                Hash = duplicate.Key
                            })
                            .ToArray()
                    });

                    groupsFound++;
                }
            }

            stopwatch.Stop();

            return new DuplicateScanResult
            {
                Groups = groups
                    .OrderByDescending(g => g.WastedBytes)
                    .Take(MaxGroups)
                    .ToArray(),
                ScannedFiles = scannedFiles,
                ScannedBytes = scannedBytes,
                WasCancelled = false,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            _logger.Info("Storage", "Busca de duplicados cancelada pelo usuario.");
        }

        stopwatch.Stop();

        return new DuplicateScanResult
        {
            Groups = groups
                .OrderByDescending(g => g.WastedBytes)
                .Take(MaxGroups)
                .ToArray(),
            ScannedFiles = scannedFiles,
            ScannedBytes = scannedBytes,
            WasCancelled = wasCancelled,
            Duration = stopwatch.Elapsed
        };
    }

    private Dictionary<long, List<FileEntry>> CollectBySize(
        IReadOnlyList<string> roots,
        long minimumSize,
        ref int scannedFiles,
        ref long scannedBytes,
        CancellationToken cancellationToken)
    {
        var bySize = new Dictionary<long, List<FileEntry>>();
        var pending = new Queue<string>();

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

                    scannedFiles++;
                    scannedBytes += length;

                    if (length < minimumSize)
                    {
                        continue;
                    }

                    if (bySize.TryGetValue(length, out var bucket) is false)
                    {
                        bucket = new List<FileEntry>();
                        bySize[length] = bucket;
                    }

                    if (bucket.Count < MaxFilesPerGroup)
                    {
                        bucket.Add(new FileEntry(file.FullName, length, lastWrite));
                    }
                }

                foreach (var child in info.EnumerateDirectories("*", ScanOptions))
                {
                    pending.Enqueue(child.FullName);
                }
            }
            catch (Exception exception)
            {
                _logger.Debug("Storage", "Pasta ignorada na busca de duplicados: " + directory + " (" + exception.GetType().Name + ").");
                continue;
            }
        }

        return bySize;
    }

    private static string? TryQuickHash(FileEntry entry)
    {
        try
        {
            using var stream = new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
            var length = (int)Math.Min(QuickHashBytes, entry.SizeBytes);
            if (length <= 0)
            {
                return null;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                var read = stream.ReadAtLeast(buffer.AsSpan(0, length), length, throwOnEndOfStream: false);
                return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TryFullHash(FileEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        break;
                    }

                    hash.AppendData(buffer, 0, read);
                }

                return Convert.ToHexString(hash.GetHashAndReset());
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private readonly record struct FileEntry(string FullPath, long SizeBytes, DateTime LastWriteTimeUtc);
}
