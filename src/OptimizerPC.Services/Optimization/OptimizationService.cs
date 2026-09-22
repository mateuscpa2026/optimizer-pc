using System.Diagnostics;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;
using OptimizerPC.Core.Security;
using OptimizerPC.Services.Cleaning;

namespace OptimizerPC.Services.Optimization;

/// <summary>
/// Executa os planos de otimizacao. O plano e sempre montado a partir das recomendacoes
/// (nunca de caminhos ou comandos informados pela interface), cada acao passa pelo
/// servico responsavel e todo ajuste de registro ou de energia registra um ponto de
/// reversao antes de ser aplicado.
/// </summary>
public sealed class OptimizationService : IOptimizationService
{
    private const string LogCategory = "Optimization";
    private const string HistoryCategory = "History.Category.Optimization";
    private const string IpConfig = "ipconfig.exe";

    private readonly ICleanupService _cleanup;
    private readonly IVisualEffectsService _visualEffects;
    private readonly IPowerService _power;
    private readonly IDriveHealthService _driveHealth;
    private readonly IStorageAnalyzer _storage;
    private readonly ICommandExecutionService _commands;
    private readonly IRestorePointManager _restorePoints;
    private readonly IHistoryService _history;
    private readonly IElevationService _elevation;
    private readonly ILocalizer _localizer;
    private readonly IAppLogger _logger;

    public OptimizationService(
        ICleanupService cleanup,
        IVisualEffectsService visualEffects,
        IPowerService power,
        IDriveHealthService driveHealth,
        IStorageAnalyzer storage,
        ICommandExecutionService commands,
        IRestorePointManager restorePoints,
        IHistoryService history,
        IElevationService elevation,
        ILocalizer localizer,
        IAppLogger logger)
    {
        _cleanup = cleanup;
        _visualEffects = visualEffects;
        _power = power;
        _driveHealth = driveHealth;
        _storage = storage;
        _commands = commands;
        _restorePoints = restorePoints;
        _history = history;
        _elevation = elevation;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task<OptimizationPlan> BuildPlanAsync(
        IReadOnlyList<Recommendation> recommendations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recommendations);

        var actions = new List<OptimizationAction>();

        foreach (var recommendation in recommendations)
        {
            var action = await CreateActionAsync(recommendation, cancellationToken).ConfigureAwait(false);
            if (action is not null)
            {
                actions.Add(action);
            }
        }

        _logger.Info(LogCategory, "Plano montado com " + actions.Count + " acao(oes) a partir de " + recommendations.Count + " recomendacao(oes).");

        return new OptimizationPlan
        {
            Actions = actions,
            Origin = "SmartOptimization",
            CreateRestorePoint = true
        };
    }

    public async Task<OptimizationPlanResult> ExecuteAsync(
        OptimizationPlan plan,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var result = new OptimizationPlanResult { PlanId = plan.Id };
        var selected = plan.Actions.Where(action => action.IsSelected && action.IsBlocked is false).ToList();

        if (selected.Count == 0)
        {
            result.RestorePointMessage = "Optimization.RestorePoint.NotNeeded";
            _logger.Info(LogCategory, "Plano sem acoes selecionadas: nada foi executado.");
            return result;
        }

        if (plan.CreateRestorePoint)
        {
            await TryCreateRestorePointAsync(result, cancellationToken).ConfigureAwait(false);
        }

        var completed = 0;

        try
        {
            foreach (var action in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();

                action.State = ActionState.Running;
                progress?.Report(new OptimizationProgress(completed, selected.Count, action.TitleKey, _localizer[action.TitleKey]));

                var execution = await DispatchAsync(action, cancellationToken).ConfigureAwait(false);

                action.State = execution.Skipped
                    ? ActionState.Skipped
                    : execution.Success ? ActionState.Succeeded : ActionState.Failed;

                result.Results.Add(execution);
                completed++;
                progress?.Report(new OptimizationProgress(completed, selected.Count, action.TitleKey, _localizer[action.TitleKey]));
            }
        }
        catch (OperationCanceledException)
        {
            result.WasCancelled = true;
            _logger.Info(LogCategory, "Execucao do plano cancelada pelo usuario.");
        }

        result.CompletedAtUtc = DateTime.UtcNow;

        _logger.Info(
            LogCategory,
            "Plano " + plan.Origin + " concluido: " + result.SucceededCount + " concluida(s), "
            + result.FailedCount + " falha(s), " + result.SkippedCount + " ignorada(s), "
            + Humanize.Bytes(result.TotalBytesFreed) + " liberado(s).");

        await RegisterHistoryAsync(plan, result, cancellationToken).ConfigureAwait(false);

        return result;
    }

    public Task<ActionExecutionResult> UndoAsync(OptimizationAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        // A reversao real usa o ponto de registro gravado antes da alteracao, o que
        // devolve o valor exato que existia. Isso e feito na Central de Restauracao.
        _logger.Info(LogCategory, "Reversao de " + action.Kind + " encaminhada para a Central de Restauracao.");

        return Task.FromResult(new ActionExecutionResult
        {
            ActionId = action.Id,
            Kind = action.Kind,
            TitleKey = action.TitleKey,
            Skipped = true,
            MessageKey = "Optimization.Undo.UseRestoreCenter",
            Detail = _localizer["Optimization.Undo.Detail"]
        });
    }

    private async Task<OptimizationAction?> CreateActionAsync(Recommendation recommendation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recommendation.ActionId) ||
            Enum.TryParse<OptimizationActionKind>(recommendation.ActionId, out var kind) is false)
        {
            return null;
        }

        var elevation = ElevationOf(kind);
        string? blockedReason = null;

        if (elevation == ElevationRequirement.Required && _elevation.IsElevated is false)
        {
            blockedReason = "Optimization.Blocked.NeedsElevation";
        }

        var parameter = await ResolveParameterAsync(kind, cancellationToken).ConfigureAwait(false);
        if (kind == OptimizationActionKind.SetPowerPlan && parameter is null)
        {
            blockedReason = "Optimization.Blocked.PlanUnavailable";
        }

        return new OptimizationAction
        {
            Id = recommendation.Id,
            Kind = kind,
            TitleKey = recommendation.TitleKey,
            DescriptionKey = recommendation.DescriptionKey,
            Impact = recommendation.Impact,
            Risk = recommendation.Risk,
            Elevation = elevation,
            EstimatedBytes = recommendation.EstimatedGainBytes,
            RequiresConfirmation = recommendation.Risk != RiskLevel.Low,
            Parameter = parameter,
            BlockedReasonKey = blockedReason,
            IsSelected = blockedReason is null
        };
    }

    private async Task<string?> ResolveParameterAsync(OptimizationActionKind kind, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case OptimizationActionKind.SetPowerPlan:
            {
                var plans = await _power.GetPlansAsync(cancellationToken).ConfigureAwait(false);
                return plans
                    .FirstOrDefault(plan => plan.Kind == PowerPlanKind.HighPerformance)?
                    .SchemeGuid.ToString("D");
            }

            case OptimizationActionKind.TrimVolume:
            {
                var volumes = await _storage.GetVolumesAsync(cancellationToken).ConfigureAwait(false);
                return (volumes.FirstOrDefault(volume => volume.IsSystemDrive) ?? volumes.FirstOrDefault())?.RootPath;
            }

            default:
                return null;
        }
    }

    private static ElevationRequirement ElevationOf(OptimizationActionKind kind) => kind switch
    {
        OptimizationActionKind.CleanUpdateCache => ElevationRequirement.Required,
        OptimizationActionKind.TrimVolume => ElevationRequirement.Required,
        OptimizationActionKind.CleanTemporaryFiles => ElevationRequirement.Recommended,
        OptimizationActionKind.CleanOldLogs => ElevationRequirement.Recommended,
        OptimizationActionKind.CleanErrorReports => ElevationRequirement.Recommended,
        _ => ElevationRequirement.None
    };

    private async Task<ActionExecutionResult> DispatchAsync(OptimizationAction action, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            return action.Kind switch
            {
                OptimizationActionKind.CleanTemporaryFiles =>
                    await CleanAsync(action, cancellationToken, "user-temp", "windows-temp").ConfigureAwait(false),

                OptimizationActionKind.CleanBrowserCache =>
                    await CleanAsync(action, cancellationToken, "browser-caches").ConfigureAwait(false),

                OptimizationActionKind.CleanThumbnails =>
                    await CleanAsync(action, cancellationToken, "thumbnail-cache").ConfigureAwait(false),

                OptimizationActionKind.CleanOldLogs =>
                    await CleanAsync(action, cancellationToken, "maintenance-logs", "component-logs").ConfigureAwait(false),

                OptimizationActionKind.CleanErrorReports =>
                    await CleanAsync(action, cancellationToken, "error-reports").ConfigureAwait(false),

                OptimizationActionKind.CleanUpdateCache =>
                    await CleanAsync(action, cancellationToken, "windows-update-cache").ConfigureAwait(false),

                OptimizationActionKind.EmptyRecycleBin =>
                    Rebranch(await _cleanup.EmptyRecycleBinAsync(cancellationToken).ConfigureAwait(false), action),

                OptimizationActionKind.DisableVisualEffects =>
                    Rebranch(await _visualEffects.ApplyAsync(true, cancellationToken).ConfigureAwait(false), action),

                OptimizationActionKind.EnableVisualEffects =>
                    Rebranch(await _visualEffects.ApplyAsync(false, cancellationToken).ConfigureAwait(false), action),

                OptimizationActionKind.SetPowerPlan =>
                    await SetPowerPlanAsync(action, cancellationToken).ConfigureAwait(false),

                OptimizationActionKind.TrimVolume =>
                    await TrimVolumeAsync(action, cancellationToken).ConfigureAwait(false),

                OptimizationActionKind.FlushDnsCache =>
                    await FlushDnsAsync(action, cancellationToken).ConfigureAwait(false),

                _ => Skipped(action, "Optimization.Skipped.ManualAction")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory, "Falha ao executar a acao " + action.Kind + ".", ex);

            return new ActionExecutionResult
            {
                ActionId = action.Id,
                Kind = action.Kind,
                TitleKey = action.TitleKey,
                MessageKey = "Optimization.Error.ActionFailed",
                Detail = ex.Message
            };
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private async Task<ActionExecutionResult> CleanAsync(
        OptimizationAction action,
        CancellationToken cancellationToken,
        params string[] targetIds)
    {
        var targets = CleanupTargetSelection.FromIds(targetIds);
        var summary = await _cleanup.CleanAsync(targets, null, cancellationToken).ConfigureAwait(false);

        var failures = summary.Results.Where(item => item.Succeeded is false).ToList();
        var bytes = summary.TotalDeletedBytes;
        var concluded = summary.Results.Count(item => item.Succeeded);

        var skipped = bytes == 0 && failures.Count == 0;
        var messageKey = skipped
            ? "Optimization.Result.NothingToDo"
            : failures.Count > 0 && bytes == 0
                ? failures[0].MessageKey
                : "Optimization.Result.Cleaned";

        return new ActionExecutionResult
        {
            ActionId = action.Id,
            Kind = action.Kind,
            TitleKey = action.TitleKey,
            Success = skipped is false && (bytes > 0 || failures.Count == 0),
            Skipped = skipped,
            MessageKey = messageKey,
            Detail = _localizer.Format("Optimization.Result.Detail", summary.TotalDeletedText, summary.TotalDeletedFiles, concluded),
            BytesFreed = bytes,
            Duration = summary.Duration
        };
    }

    private async Task<ActionExecutionResult> SetPowerPlanAsync(OptimizationAction action, CancellationToken cancellationToken)
    {
        var plans = await _power.GetPlansAsync(cancellationToken).ConfigureAwait(false);

        PowerPlanInfo? plan = null;
        if (action.Parameter is not null && Guid.TryParse(action.Parameter, out var scheme))
        {
            plan = plans.FirstOrDefault(item => item.SchemeGuid == scheme);
        }

        plan ??= plans.FirstOrDefault(item => item.Kind == PowerPlanKind.HighPerformance);

        if (plan is null)
        {
            return Skipped(action, "Optimization.Skipped.PlanUnavailable");
        }

        return Rebranch(await _power.SetActivePlanAsync(plan, cancellationToken).ConfigureAwait(false), action);
    }

    private async Task<ActionExecutionResult> TrimVolumeAsync(OptimizationAction action, CancellationToken cancellationToken)
    {
        var volumes = await _storage.GetVolumesAsync(cancellationToken).ConfigureAwait(false);

        VolumeInfo? volume = null;
        if (string.IsNullOrWhiteSpace(action.Parameter) is false)
        {
            volume = volumes.FirstOrDefault(item =>
                string.Equals(item.RootPath, action.Parameter, StringComparison.OrdinalIgnoreCase));
        }

        volume ??= volumes.FirstOrDefault(item => item.IsSystemDrive) ?? volumes.FirstOrDefault();

        if (volume is null)
        {
            return Skipped(action, "Optimization.Skipped.VolumeUnavailable");
        }

        var outcome = await _driveHealth.RunTrimAsync(volume, cancellationToken).ConfigureAwait(false);

        return new ActionExecutionResult
        {
            ActionId = action.Id,
            Kind = action.Kind,
            TitleKey = action.TitleKey,
            Success = outcome.Started && outcome.ExitCode == 0,
            Skipped = outcome.Started is false && outcome.RequiresElevation,
            MessageKey = outcome.MessageKey,
            Detail = FirstLine(outcome.Output)
        };
    }

    private async Task<ActionExecutionResult> FlushDnsAsync(OptimizationAction action, CancellationToken cancellationToken)
    {
        var allowed = CommandAllowList.All.FirstOrDefault(command =>
            string.Equals(command.Executable, IpConfig, StringComparison.OrdinalIgnoreCase));

        if (allowed is null)
        {
            return Skipped(action, "Tools.Error.NotAllowed");
        }

        var outcome = await _commands
            .RunAllowedAsync(CommandAllowList.ResolveExecutablePath(allowed), new[] { "/flushdns" }, cancellationToken)
            .ConfigureAwait(false);

        return new ActionExecutionResult
        {
            ActionId = action.Id,
            Kind = action.Kind,
            TitleKey = action.TitleKey,
            Success = outcome.Started && outcome.ExitCode == 0,
            MessageKey = outcome.MessageKey,
            Detail = FirstLine(outcome.StandardOutput)
        };
    }

    private async Task TryCreateRestorePointAsync(OptimizationPlanResult result, CancellationToken cancellationToken)
    {
        try
        {
            var point = await _restorePoints
                .CreateAsync(_localizer["Optimization.RestorePoint.Description"], cancellationToken)
                .ConfigureAwait(false);

            result.RestorePointCreated = point.Success;
            result.RestorePointMessage = point.MessageKey;
        }
        catch (Exception ex)
        {
            // Sem ponto de restauracao o plano segue: cada alteracao individual ainda
            // registra seu proprio ponto de reversao no banco local.
            _logger.Warning(LogCategory, "Nao foi possivel criar o ponto de restauracao do sistema.", ex);
            result.RestorePointMessage = "Optimization.RestorePoint.Failed";
        }
    }

    private async Task RegisterHistoryAsync(OptimizationPlan plan, OptimizationPlanResult result, CancellationToken cancellationToken)
    {
        try
        {
            await _history.RecordAsync(
                new HistoryEntry
                {
                    Category = HistoryCategory,
                    Action = _localizer[plan.Origin == "LowEndMode" ? "History.Optimization.LowEnd" : "History.Optimization.Plan"],
                    Description = _localizer.Format(
                        "History.Optimization.Description",
                        result.Results.Count,
                        Humanize.Bytes(result.TotalBytesFreed)),
                    Result = _localizer[ResultKeyOf(result)],
                    Success = result.FailedCount == 0,
                    BytesFreed = result.TotalBytesFreed,
                    Details = result.WasCancelled ? _localizer["History.Optimization.Cancelled"] : null
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategory, "Nao foi possivel registrar a execucao do plano no historico.", ex);
        }
    }

    private static string ResultKeyOf(OptimizationPlanResult result)
    {
        if (result.WasCancelled)
        {
            return "History.Optimization.Cancelled";
        }

        if (result.FailedCount > 0)
        {
            return "History.Optimization.Partial";
        }

        return result.SucceededCount > 0 ? "History.Optimization.Ok" : "History.Optimization.NothingDone";
    }

    private static ActionExecutionResult Skipped(OptimizationAction action, string messageKey) => new()
    {
        ActionId = action.Id,
        Kind = action.Kind,
        TitleKey = action.TitleKey,
        Skipped = true,
        MessageKey = messageKey
    };

    /// <summary>
    /// Repassa o resultado de um servico para a acao do plano, mantendo o identificador
    /// e o titulo usados na tela de execucao.
    /// </summary>
    private static ActionExecutionResult Rebranch(ActionExecutionResult source, OptimizationAction action) => new()
    {
        ActionId = action.Id,
        Kind = action.Kind,
        TitleKey = action.TitleKey,
        Success = source.Success,
        Skipped = source.Skipped,
        MessageKey = source.MessageKey,
        Detail = source.Detail,
        BytesFreed = source.BytesFreed,
        Duration = source.Duration,
        RestoreRecordId = source.RestoreRecordId
    };

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var index = text.IndexOfAny(new[] { '\r', '\n' });
        var line = index < 0 ? text : text[..index];
        return line.Length <= 200 ? line.Trim() : line[..200].Trim();
    }
}
