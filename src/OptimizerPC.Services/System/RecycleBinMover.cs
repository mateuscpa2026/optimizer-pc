using System.Runtime.Versioning;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;
using OptimizerPC.Core.Security;
using OptimizerPC.Services.Interop;

namespace OptimizerPC.Services.System;

/// <summary>
/// Envia arquivos para a Lixeira do Windows a pedido explicito do usuario.
/// Nada aqui exclui permanentemente: a operacao e reversivel pela propria Lixeira.
/// Todo caminho passa por <see cref="SafePathValidator"/> antes de qualquer alteracao,
/// de modo que areas do sistema e conteudo pessoal sao recusados com um motivo claro.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RecycleBinMover : IRecycleBinMover
{
    private const string LogCategory = "Storage";
    private const string HistoryCategory = "History.Category.Cleanup";

    private readonly SafePathValidator _validator;
    private readonly IHistoryService _history;
    private readonly ILocalizer _localizer;
    private readonly IAppLogger _logger;

    public RecycleBinMover(
        SafePathValidator validator,
        IHistoryService history,
        ILocalizer localizer,
        IAppLogger logger)
    {
        _validator = validator;
        _history = history;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task<FileMoveResult> MoveToRecycleBinAsync(
        IReadOnlyList<string> paths,
        IReadOnlyList<string> allowedRoots,
        IProgress<StorageScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(allowedRoots);

        var result = await RunOnStaThreadAsync(() => Move(paths, allowedRoots, progress, cancellationToken))
            .ConfigureAwait(false);

        await RecordAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Decide se um caminho pode ser enviado para a Lixeira. Delega para
    /// <see cref="RecycleBinGuard"/>, a mesma regra usada pela interface.
    /// </summary>
    public static bool TryAuthorize(
        string? path,
        IReadOnlyList<string> allowedRoots,
        SafePathValidator validator,
        out string normalizedPath,
        out string reasonKey)
        => RecycleBinGuard.TryAuthorize(path, allowedRoots, validator, out normalizedPath, out reasonKey);

    private FileMoveResult Move(
        IReadOnlyList<string> paths,
        IReadOnlyList<string> allowedRoots,
        IProgress<StorageScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<FileMoveOutcome>();
        var processed = 0;
        var movedBytes = 0L;
        var cancelled = false;

        foreach (var candidate in paths)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            processed++;
            progress?.Report(new StorageScanProgress(
                candidate,
                processed,
                movedBytes,
                paths.Count == 0 ? 0 : processed * 100d / paths.Count));

            if (TryAuthorize(candidate, allowedRoots, _validator, out var normalized, out var reasonKey) is false)
            {
                outcomes.Add(new FileMoveOutcome
                {
                    Path = normalized,
                    WasBlocked = true,
                    ReasonKey = reasonKey
                });

                _logger.Info(LogCategory, "Envio para a Lixeira recusado pela validacao de seguranca (" + reasonKey + "): " + normalized);
                continue;
            }

            var sizeBytes = TryGetSize(normalized);
            var code = NativeShell.TryMoveToRecycleBin(normalized);

            if (code == 0)
            {
                outcomes.Add(new FileMoveOutcome
                {
                    Path = normalized,
                    SizeBytes = sizeBytes,
                    Moved = true
                });

                movedBytes += sizeBytes;
            }
            else
            {
                outcomes.Add(new FileMoveOutcome
                {
                    Path = normalized,
                    SizeBytes = sizeBytes,
                    ReasonKey = MapFailure(code)
                });

                _logger.Warning(LogCategory, "Falha ao enviar para a Lixeira (codigo " + code + "): " + normalized);
            }
        }

        return new FileMoveResult
        {
            Outcomes = outcomes,
            WasCancelled = cancelled,
            MovedFiles = outcomes.Where(outcome => outcome.Moved).ToList(),
            BlockedFiles = outcomes.Where(outcome => outcome.WasBlocked).ToList(),
            FailedFiles = outcomes.Where(outcome => outcome.Moved is false && outcome.WasBlocked is false).ToList(),
            MovedBytes = movedBytes
        };
    }

    private static string MapFailure(int code) => code switch
    {
        NativeShell.DE_OPCANCELLED => "Storage.Selection.Failed.Cancelled",
        NativeShell.DE_ACCESSDENIEDSRC => "Storage.Selection.Failed.AccessDenied",
        _ => "Storage.Selection.Failed.Generic"
    };

    private static long TryGetSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private async Task RecordAsync(FileMoveResult result, CancellationToken cancellationToken)
    {
        if (result.MovedCount == 0)
        {
            return;
        }

        try
        {
            await _history.RecordAsync(
                new HistoryEntry
                {
                    Category = HistoryCategory,
                    Action = _localizer["Storage.Selection.Action"],
                    Description = _localizer.Format("Storage.Selection.History", result.MovedCount, Humanize.Bytes(result.MovedBytes)),
                    Result = _localizer["Storage.Selection.History.Result"],
                    Success = result.FailedCount == 0,
                    BytesFreed = result.MovedBytes
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategory, "Nao foi possivel registrar a operacao no historico.", ex);
        }
    }

    /// <summary>
    /// A shell do Windows exige um thread STA para operacoes de arquivo.
    /// </summary>
    private static Task<FileMoveResult> RunOnStaThreadAsync(Func<FileMoveResult> work)
    {
        var completion = new TaskCompletionSource<FileMoveResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(work());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "OptimizerPC.RecycleBin"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task;
    }
}
