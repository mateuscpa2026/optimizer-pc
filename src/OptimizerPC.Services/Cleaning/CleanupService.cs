using System.Diagnostics;
using System.Runtime.InteropServices;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;
using OptimizerPC.Core.Security;
using OptimizerPC.Services.Interop;

namespace OptimizerPC.Services.Cleaning;

/// <summary>
/// Executa a limpeza aprovada pelo usuario. Cada alvo e resolvido novamente pelo
/// catalogo interno (os caminhos informados pela interface sao ignorados), cada arquivo
/// passa pela validacao de seguranca e cada categoria gera um registro no historico
/// com o espaco efetivamente liberado.
/// </summary>
public sealed class CleanupService : ICleanupService
{
    private const string LogCategory = "Cleanup";
    private const string HistoryCategory = "History.Category.Cleanup";
    private const int MaxFailuresPerTarget = 40;
    private const int MaxDirectoryDepth = 24;
    private const int MaxDirectoriesPerTarget = 20_000;

    private readonly IFileSystemService _files;
    private readonly SafePathValidator _validator;
    private readonly IElevationService _elevation;
    private readonly IHistoryService _history;
    private readonly ISettingsService _settings;
    private readonly ILocalizer _localizer;
    private readonly IAppLogger _logger;

    public CleanupService(
        IFileSystemService files,
        SafePathValidator validator,
        IElevationService elevation,
        IHistoryService history,
        ISettingsService settings,
        ILocalizer localizer,
        IAppLogger logger)
    {
        _files = files;
        _validator = validator;
        _elevation = elevation;
        _history = history;
        _settings = settings;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task<CleanupSummary> CleanAsync(
        IReadOnlyList<CleanupTarget> targets,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var summary = new CleanupSummary();
        var approved = targets.Where(target => target.IsSelected && target.IsAvailable).ToList();

        if (approved.Count != targets.Count)
        {
            _logger.Info(LogCategory, (targets.Count - approved.Count) + " alvo(s) nao selecionado(s) ou indisponivel(is) foram ignorados.");
        }

        var state = new CleanupRunState
        {
            TotalTargets = approved.Count,
            TotalBytes = approved.Sum(target => target.TotalBytes)
        };

        try
        {
            foreach (var target in approved)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await CleanTargetAsync(target, state, progress, cancellationToken).ConfigureAwait(false);
                summary.Results.Add(result);

                state.CompletedTargets++;
                state.ProcessedBytes += result.DeletedBytes;

                await RegisterHistoryAsync(target, result, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            summary.WasCancelled = true;
            _logger.Info(LogCategory, "Limpeza cancelada pelo usuario.");
        }

        summary.CompletedAtUtc = DateTime.UtcNow;

        if (summary.TotalDeletedBytes > 0)
        {
            await RememberLastCleanupAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.Info(
            LogCategory,
            "Limpeza concluida: " + summary.TotalDeletedText + " em " + summary.Results.Count + " categoria(s).");

        return summary;
    }

    public async Task<ActionExecutionResult> EmptyRecycleBinAsync(CancellationToken cancellationToken = default)
    {
        var result = EmptyRecycleBinCore();

        await _history.RecordAsync(
            new HistoryEntry
            {
                Category = HistoryCategory,
                Action = _localizer["Cleanup.Target.RecycleBin.Title"],
                Description = _localizer.Format("Cleanup.History.RecycleBin", Humanize.Bytes(result.BytesFreed)),
                Result = _localizer[result.MessageKey],
                Success = result.Success,
                BytesFreed = result.BytesFreed
            },
            cancellationToken).ConfigureAwait(false);

        if (result.BytesFreed > 0)
        {
            await RememberLastCleanupAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    // A remocao de arquivos e uma operacao sincrona de disco: sai da thread de interface.
    private Task<CleanupCategoryResult> CleanTargetAsync(
        CleanupTarget target,
        CleanupRunState state,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => CleanTarget(target, state, progress, cancellationToken), cancellationToken);

    private CleanupCategoryResult CleanTarget(
        CleanupTarget target,
        CleanupRunState state,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new CleanupCategoryResult { TargetId = target.Id, Category = target.Category };

        try
        {
            var descriptor = CleanupTargetCatalog.Find(target.Id);
            if (descriptor is null)
            {
                _logger.Warning(LogCategory, "Alvo de limpeza desconhecido foi ignorado: " + target.Id + ".");
                result.Succeeded = false;
                result.MessageKey = "Cleanup.Error.UnknownTarget";
                return result;
            }

            if (descriptor.IsRecycleBin)
            {
                var emptied = EmptyRecycleBinCore();
                result.Succeeded = emptied.Success;
                result.MessageKey = emptied.MessageKey;
                result.DeletedBytes = emptied.BytesFreed;
                result.DeletedFiles = emptied.BytesFreed > 0 ? 1 : 0;
                return result;
            }

            if (descriptor.Elevation == ElevationRequirement.Required && _elevation.IsElevated is false)
            {
                result.Succeeded = false;
                result.MessageKey = "Cleanup.Reason.NeedsElevation";
                return result;
            }

            var locations = CleanupPathResolver.Resolve(_files, descriptor);
            if (locations.Count == 0)
            {
                result.Succeeded = false;
                result.MessageKey = "Cleanup.Reason.LocationMissing";
                return result;
            }

            foreach (var location in locations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var context = new SafeDeleteContext(
                    new[] { location.Path },
                    AllowPersonalContent: true,
                    AllowProtectedSystemPaths: true);

                if (location.Location.IsSingleFile)
                {
                    TryDeleteFile(location.Path, context, result, state, descriptor, progress, cancellationToken);
                    continue;
                }

                foreach (var file in _files.EnumerateFiles(location.Path, recursive: true))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (CleanupFileFilter.Matches(location.Location, file) is false)
                    {
                        continue;
                    }

                    TryDeleteFile(file.Path, context, result, state, descriptor, progress, cancellationToken);
                }

                RemoveEmptyDirectories(location.Path, context, result, descriptor, cancellationToken);
            }

            result.MessageKey = BuildMessageKey(result);
            result.Succeeded = result.DeletedFiles > 0 || result.Failures.Count == 0;
            return result;
        }
        finally
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
        }
    }

    private void TryDeleteFile(
        string path,
        SafeDeleteContext context,
        CleanupCategoryResult result,
        CleanupRunState state,
        CleanupTargetDescriptor descriptor,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var validation = _validator.ValidateForDeletion(path, context);
        if (validation.IsAllowed is false)
        {
            RegisterFailure(result, validation.ResolvedPath, validation.ReasonKey);
            return;
        }

        var name = Path.GetFileName(validation.ResolvedPath);
        if (ProtectedPaths.ProtectedFileNamesList.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            RegisterFailure(result, validation.ResolvedPath, "Security.Reason.ProtectedFile");
            return;
        }

        long size;
        try
        {
            size = _files.GetFileSize(validation.ResolvedPath);
            _files.DeleteFile(validation.ResolvedPath);
        }
        catch (Exception ex)
        {
            RegisterFailure(result, validation.ResolvedPath, "Cleanup.Reason.DeleteFailed", ex.Message);
            return;
        }

        result.DeletedFiles++;
        result.DeletedBytes += size;
        state.ProcessedBytes += size;

        progress?.Report(new CleanupProgress(
            descriptor.Id,
            validation.ResolvedPath,
            state.ProcessedBytes,
            state.TotalBytes,
            state.CompletedTargets,
            state.TotalTargets));

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Remove apenas subpastas que ficaram vazias depois da limpeza dos arquivos.</summary>
    private void RemoveEmptyDirectories(
        string root,
        SafeDeleteContext context,
        CleanupCategoryResult result,
        CleanupTargetDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var directories = new List<string>();
        CollectDirectories(root, directories, 0, cancellationToken);

        for (var index = directories.Count - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directory = directories[index];
            if (string.Equals(directory, root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (_files.EnumerateFiles(directory, recursive: false).Any() ||
                    _files.EnumerateDirectories(directory).Any())
                {
                    continue;
                }

                var validation = _validator.ValidateForDeletion(directory, context);
                if (validation.IsAllowed is false)
                {
                    continue;
                }

                _files.DeleteDirectory(validation.ResolvedPath, recursive: false);
                _logger.Debug(LogCategory, "Subpasta vazia removida em " + descriptor.Id + ".");
            }
            catch (Exception ex)
            {
                RegisterFailure(result, directory, "Cleanup.Reason.DeleteFailed", ex.Message);
            }
        }
    }

    private void CollectDirectories(string root, List<string> directories, int depth, CancellationToken cancellationToken)
    {
        if (depth >= MaxDirectoryDepth || directories.Count >= MaxDirectoriesPerTarget)
        {
            return;
        }

        foreach (var child in _files.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (directories.Count >= MaxDirectoriesPerTarget)
            {
                return;
            }

            if (CleanupSafety.IsAllowedLocation(child) is false || IsReparsePoint(child))
            {
                continue;
            }

            directories.Add(child);
            CollectDirectories(child, directories, depth + 1, cancellationToken);
        }
    }

    private ActionExecutionResult EmptyRecycleBinCore()
    {
        var titleKey = "Cleanup.Target.RecycleBin.Title";

        try
        {
            var info = ReadRecycleBinSize();
            if (info.SizeBytes <= 0 && info.ItemCount <= 0)
            {
                return new ActionExecutionResult
                {
                    ActionId = CleanupTargetCatalog.RecycleBinId,
                    Kind = OptimizationActionKind.EmptyRecycleBin,
                    TitleKey = titleKey,
                    Skipped = true,
                    MessageKey = "Cleanup.Result.RecycleBinEmpty"
                };
            }

            const uint flags = NativeShell.SHERB_NOCONFIRMATION | NativeShell.SHERB_NOPROGRESSUI | NativeShell.SHERB_NOSOUND;
            var result = NativeShell.SHEmptyRecycleBin(IntPtr.Zero, null, flags);
            if (result != 0)
            {
                _logger.Warning(LogCategory, "Nao foi possivel esvaziar a Lixeira. Codigo: " + result + ".");
                return new ActionExecutionResult
                {
                    ActionId = CleanupTargetCatalog.RecycleBinId,
                    Kind = OptimizationActionKind.EmptyRecycleBin,
                    TitleKey = titleKey,
                    MessageKey = "Cleanup.Error.RecycleBinFailed"
                };
            }

            _logger.Info(LogCategory, "Lixeira esvaziada a pedido do usuario.");
            return new ActionExecutionResult
            {
                ActionId = CleanupTargetCatalog.RecycleBinId,
                Kind = OptimizationActionKind.EmptyRecycleBin,
                TitleKey = titleKey,
                Success = true,
                MessageKey = "Cleanup.Result.RecycleBinEmptied",
                BytesFreed = info.SizeBytes
            };
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory, "Falha ao esvaziar a Lixeira.", ex);
            return new ActionExecutionResult
            {
                ActionId = CleanupTargetCatalog.RecycleBinId,
                Kind = OptimizationActionKind.EmptyRecycleBin,
                TitleKey = titleKey,
                MessageKey = "Cleanup.Error.RecycleBinFailed"
            };
        }
    }

    private async Task RegisterHistoryAsync(CleanupTarget target, CleanupCategoryResult result, CancellationToken cancellationToken)
    {
        var title = _localizer[target.TitleKey];

        await _history.RecordAsync(
            new HistoryEntry
            {
                Category = HistoryCategory,
                Action = title,
                Description = _localizer.Format("Cleanup.History.Description", result.DeletedText, result.DeletedFiles),
                Result = _localizer[result.MessageKey],
                Success = result.Succeeded,
                BytesFreed = result.DeletedBytes,
                Details = result.Failures.Count == 0
                    ? null
                    : result.Failures.Count + " item(ns) preservado(s). Primeiro motivo: " + _localizer[result.Failures[0].ReasonKey]
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RememberLastCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            _settings.Current.LastCleanupUtc = DateTime.UtcNow;
            await _settings.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategory, "Nao foi possivel registrar a data da ultima limpeza.", ex);
        }
    }

    private RecycleBinInfo ReadRecycleBinSize()
    {
        var info = new NativeShell.SHQUERYRBINFO { cbSize = Marshal.SizeOf<NativeShell.SHQUERYRBINFO>() };
        return NativeShell.SHQueryRecycleBin(null, ref info) == 0
            ? new RecycleBinInfo { ItemCount = info.i64NumItems, SizeBytes = info.i64Size }
            : new RecycleBinInfo();
    }

    private void RegisterFailure(CleanupCategoryResult result, string path, string reasonKey, string detail = "")
    {
        result.SkippedFiles++;

        if (result.Failures.Count > MaxFailuresPerTarget)
        {
            return;
        }

        if (result.Failures.Count == MaxFailuresPerTarget)
        {
            result.Failures.Add(new CleanupFailure
            {
                Path = string.Empty,
                ReasonKey = "Cleanup.Reason.MoreSkipped",
                Detail = string.Empty
            });
            return;
        }

        result.Failures.Add(new CleanupFailure { Path = path, ReasonKey = reasonKey, Detail = detail });
    }

    private static string BuildMessageKey(CleanupCategoryResult result) => result switch
    {
        { DeletedFiles: > 0, Failures.Count: 0 } => "Cleanup.Result.Cleaned",
        { DeletedFiles: > 0 } => "Cleanup.Result.Partial",
        { Failures.Count: 0 } => "Cleanup.Result.NothingToDo",
        _ => "Cleanup.Result.Blocked"
    };

    private bool IsReparsePoint(string path)
    {
        try
        {
            return _files.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception)
        {
            // Em caso de duvida o diretorio e preservado: nunca seguir links durante a limpeza.
            return true;
        }
    }

    /// <summary>Contadores da execucao, usados para calcular o percentual de progresso.</summary>
    private sealed class CleanupRunState
    {
        public int TotalTargets { get; init; }
        public long TotalBytes { get; init; }
        public int CompletedTargets { get; set; }
        public long ProcessedBytes { get; set; }
    }
}
