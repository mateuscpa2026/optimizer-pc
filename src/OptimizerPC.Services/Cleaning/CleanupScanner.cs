using System.Runtime.InteropServices;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Interop;

namespace OptimizerPC.Services.Cleaning;

/// <summary>
/// Detecta os locais de limpeza e mede o espaco potencialmente recuperavel.
/// A varredura apenas le o disco: nada e removido nesta etapa.
/// </summary>
public sealed class CleanupScanner : ICleanupScanner
{
    private readonly IFileSystemService _files;
    private readonly IElevationService _elevation;
    private readonly IAppLogger _logger;

    public CleanupScanner(IFileSystemService files, IElevationService elevation, IAppLogger logger)
    {
        _files = files;
        _elevation = elevation;
        _logger = logger;
    }

    public Task<IReadOnlyList<CleanupTarget>> ScanAsync(
        IReadOnlyList<CleanupCategory>? categories = null,
        bool includeRecycleBin = true,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(categories, includeRecycleBin, progress, cancellationToken), cancellationToken);

    public Task<RecycleBinInfo> GetRecycleBinInfoAsync(CancellationToken cancellationToken = default) =>
        Task.Run(ReadRecycleBin, cancellationToken);

    private IReadOnlyList<CleanupTarget> Scan(
        IReadOnlyList<CleanupCategory>? categories,
        bool includeRecycleBin,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var descriptors = CleanupTargetCatalog.Targets
            .Where(descriptor => includeRecycleBin || descriptor.IsRecycleBin is false)
            .Where(descriptor => categories is null || categories.Count == 0 || categories.Contains(descriptor.Category))
            .ToList();

        var targets = new List<CleanupTarget>(descriptors.Count);
        var total = descriptors.Count;

        for (var index = 0; index < descriptors.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            targets.Add(ScanTarget(descriptors[index], progress, index, total, cancellationToken));
            progress?.Report(new CleanupProgress(descriptors[index].Id, string.Empty, 0, 0, index + 1, total));
        }

        return targets;
    }

    private CleanupTarget ScanTarget(
        CleanupTargetDescriptor descriptor,
        IProgress<CleanupProgress>? progress,
        int completed,
        int total,
        CancellationToken cancellationToken)
    {
        if (descriptor.IsRecycleBin)
        {
            var info = ReadRecycleBin();
            var recycleBin = CreateTarget(descriptor, Array.Empty<string>());
            recycleBin.TotalBytes = info.SizeBytes;
            recycleBin.FileCount = info.ItemCount;
            return recycleBin;
        }

        var locations = CleanupPathResolver.Resolve(_files, descriptor);
        var target = CreateTarget(descriptor, locations.Select(location => location.Path).ToArray());

        foreach (var location in locations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Measure(target, location, cancellationToken);
            progress?.Report(new CleanupProgress(descriptor.Id, location.Path, target.TotalBytes, 0, completed, total));
        }

        if (locations.Count == 0)
        {
            MarkUnavailable(target, "Cleanup.Reason.LocationMissing");
            return target;
        }

        // Sem privilegios administrativos o conteudo pode ser medido, mas nao removido.
        if (descriptor.Elevation == ElevationRequirement.Required && _elevation.IsElevated is false)
        {
            MarkUnavailable(target, "Cleanup.Reason.NeedsElevation");
        }

        return target;
    }

    private void Measure(CleanupTarget target, ResolvedCleanupLocation location, CancellationToken cancellationToken)
    {
        if (location.Location.IsSingleFile)
        {
            var size = _files.GetFileSize(location.Path);
            var lastWrite = _files.GetLastWriteTimeUtc(location.Path);
            if (CleanupFileFilter.Matches(location.Location, new FileEntry(location.Path, size, lastWrite)))
            {
                target.TotalBytes += size;
                target.FileCount++;
            }

            return;
        }

        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in _files.EnumerateFiles(location.Path, recursive: true))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (CleanupFileFilter.Matches(location.Location, file) is false)
            {
                continue;
            }

            target.TotalBytes += file.Length;
            target.FileCount++;

            var parent = Path.GetDirectoryName(file.Path);
            if (string.IsNullOrEmpty(parent) is false)
            {
                directories.Add(parent);
            }
        }

        target.DirectoryCount += directories.Count;
    }

    private RecycleBinInfo ReadRecycleBin()
    {
        try
        {
            var info = new NativeShell.SHQUERYRBINFO { cbSize = Marshal.SizeOf<NativeShell.SHQUERYRBINFO>() };
            var result = NativeShell.SHQueryRecycleBin(null, ref info);
            if (result != 0)
            {
                _logger.Warning("Cleanup", "Nao foi possivel consultar a Lixeira. Codigo: " + result + ".");
                return new RecycleBinInfo();
            }

            return new RecycleBinInfo { ItemCount = info.i64NumItems, SizeBytes = info.i64Size };
        }
        catch (Exception ex)
        {
            _logger.Error("Cleanup", "Falha ao consultar a Lixeira.", ex);
            return new RecycleBinInfo();
        }
    }

    private static CleanupTarget CreateTarget(CleanupTargetDescriptor descriptor, IReadOnlyList<string> paths) => new()
    {
        Id = descriptor.Id,
        Category = descriptor.Category,
        TitleKey = descriptor.TitleKey,
        DescriptionKey = descriptor.DescriptionKey,
        Paths = paths,
        Risk = descriptor.Risk,
        Elevation = descriptor.Elevation,
        RequiresConfirmation = descriptor.RequiresConfirmation,
        IsSelected = descriptor.SelectedByDefault
    };

    private static void MarkUnavailable(CleanupTarget target, string reasonKey)
    {
        target.IsAvailable = false;
        target.IsSelected = false;
        target.UnavailableReasonKey = reasonKey;
    }
}
