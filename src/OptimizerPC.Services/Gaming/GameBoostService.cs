using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Restore;

namespace OptimizerPC.Services.Gaming;

/// <summary>
/// Modo Gamer. A sessao aplica apenas ajustes reversiveis e temporarios: modo de jogo do
/// Windows, plano de energia de alto desempenho, efeitos visuais, prioridade do jogo,
/// interrupcao de dois servicos de indexacao e pausa das notificacoes do aplicativo.
/// Nao existe overclock, alteracao de BIOS, de firmware ou de tensao: nada do que e
/// aplicado aqui depende de hardware.
/// </summary>
public sealed class GameBoostService : IGameBoostService
{
    private const string LogCategory = "Gaming";
    private const string GameBarKey = @"Software\Microsoft\GameBar";

    /// <summary>
    /// Servicos interrompidos apenas durante a sessao e que voltam sozinhos no proximo
    /// inicio do Windows, porque o modo de inicio nunca e alterado.
    /// </summary>
    private static readonly string[] PausableServices = { "WSearch", "SysMain" };

    private readonly IPowerService _power;
    private readonly IVisualEffectsService _visualEffects;
    private readonly IRegistryService _registry;
    private readonly IProcessService _processes;
    private readonly IServiceManager _services;
    private readonly IRestoreService _restore;
    private readonly ISettingsService _settings;
    private readonly IHistoryService _history;
    private readonly ILocalizer _localizer;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();

    private ActiveSession? _active;

    public GameBoostService(
        IPowerService power,
        IVisualEffectsService visualEffects,
        IRegistryService registry,
        IProcessService processes,
        IServiceManager services,
        IRestoreService restore,
        ISettingsService settings,
        IHistoryService history,
        ILocalizer localizer,
        IAppLogger logger)
    {
        _power = power;
        _visualEffects = visualEffects;
        _registry = registry;
        _processes = processes;
        _services = services;
        _restore = restore;
        _settings = settings;
        _history = history;
        _localizer = localizer;
        _logger = logger;
    }

    public GameBoostSession? CurrentSession
    {
        get
        {
            lock (_stateLock)
            {
                return _active?.Request;
            }
        }
    }

    public async Task<GameBoostResult> StartAsync(
        GameBoostSession session,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_active is not null)
            {
                return new GameBoostResult { Success = false, MessageKey = "Gamer.Error.AlreadyRunning" };
            }

            var results = new List<ActionExecutionResult>();
            var previousPlan = await _power.GetActivePlanAsync(cancellationToken).ConfigureAwait(false);
            var visualEffectsChanged = false;
            var reports = 0;

            void Report(string titleKey)
            {
                reports++;
                progress?.Report(new OptimizationProgress(reports, 8, titleKey, _localizer[titleKey]));
            }

            RegistryValueData? previousAutoGameMode = null;
            RegistryValueData? previousAllowAutoGameMode = null;

            if (session.EnableGameMode)
            {
                Report("Gamer.Action.GameMode.Title");

                // Os valores sao lidos antes da alteracao: e esse estado que permite desfazer.
                previousAutoGameMode = _registry.GetValue(RegistryHiveKind.CurrentUser, GameBarKey, "AutoGameModeEnabled");
                previousAllowAutoGameMode = _registry.GetValue(RegistryHiveKind.CurrentUser, GameBarKey, "AllowAutoGameMode");

                results.Add(EnableGameMode());
                await RegisterGameModeRecordsAsync(previousAutoGameMode, previousAllowAutoGameMode, cancellationToken).ConfigureAwait(false);
            }

            if (session.SetHighPerformancePowerPlan)
            {
                Report("Gamer.Action.PowerPlan.Title");
                results.Add(await ApplyPowerPlanAsync(cancellationToken).ConfigureAwait(false));
            }

            if (session.DisableVisualEffects)
            {
                Report("Gamer.Action.VisualEffects.Title");

                var outcome = await _visualEffects.ApplyAsync(true, cancellationToken).ConfigureAwait(false);
                visualEffectsChanged = outcome.Success;
                results.Add(Rebranch(outcome, "gamer.visual-effects", OptimizationActionKind.DisableVisualEffects, "Gamer.Action.VisualEffects.Title"));
            }

            if (session.ReduceBackgroundProcessPriority)
            {
                Report("Gamer.Action.Priority.Title");
                results.Add(await ApplyPriorityAsync(session, cancellationToken).ConfigureAwait(false));
            }

            if (session.StopTemporaryServices)
            {
                Report("Gamer.Action.Services.Title");
                results.Add(await PauseServicesAsync(cancellationToken).ConfigureAwait(false));
            }

            var previousNotifications = _settings.Current.NotificationsEnabled;
            var notificationsPaused = false;

            if (session.PauseNotifications && previousNotifications)
            {
                Report("Gamer.Action.Notifications.Title");

                _settings.Current.NotificationsEnabled = false;
                await _settings.SaveAsync(cancellationToken).ConfigureAwait(false);
                notificationsPaused = true;

                results.Add(new ActionExecutionResult
                {
                    ActionId = "gamer.notifications",
                    Kind = OptimizationActionKind.SetServiceStartMode,
                    TitleKey = "Gamer.Action.Notifications.Title",
                    Success = true,
                    MessageKey = "Gamer.Result.NotificationsPaused"
                });
            }

            _active = new ActiveSession
            {
                Request = session,
                PreviousPlan = previousPlan,
                PreviousAutoGameMode = previousAutoGameMode,
                PreviousAllowAutoGameMode = previousAllowAutoGameMode,
                PreviousNotificationsEnabled = previousNotifications,
                NotificationsPaused = notificationsPaused,
                VisualEffectsChanged = visualEffectsChanged,
                StartedAtUtc = DateTime.UtcNow
            };

            var succeeded = results.Count(result => result.Success);
            var failed = results.Count(result => result is { Success: false, Skipped: false });

            await RecordHistoryAsync(
                "History.Gamer.Start",
                _localizer.Format("History.Gamer.StartDescription", session.GameName ?? _localizer["Gamer.NoGame"]),
                failed == 0 ? "History.Gamer.Ok" : "History.Gamer.Partial",
                failed == 0,
                cancellationToken).ConfigureAwait(false);

            _logger.Info(LogCategory, "Sessao do Modo Gamer iniciada com " + succeeded + " ajuste(s) aplicado(s).");

            return new GameBoostResult
            {
                Success = succeeded > 0,
                Results = results,
                MessageKey = failed == 0 ? "Gamer.Result.Started" : "Gamer.Result.StartedPartial"
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GameBoostResult> StopAsync(bool restoreEverything, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_active is null)
            {
                return new GameBoostResult { Success = false, MessageKey = "Gamer.Error.NotRunning" };
            }

            var session = _active;
            var results = new List<ActionExecutionResult>();

            if (restoreEverything)
            {
                if (session.VisualEffectsChanged)
                {
                    var outcome = await _visualEffects.ApplyAsync(false, cancellationToken).ConfigureAwait(false);
                    results.Add(Rebranch(outcome, "gamer.visual-effects", OptimizationActionKind.EnableVisualEffects, "Gamer.Action.VisualEffects.Title"));
                }

                if (session.PreviousPlan is not null)
                {
                    results.Add(await RestorePowerPlanAsync(session.PreviousPlan, cancellationToken).ConfigureAwait(false));
                }

                if (session.Request.EnableGameMode)
                {
                    var restored =
                        RegistryValueWriter.Restore(_registry, RegistryHiveKind.CurrentUser, GameBarKey, "AutoGameModeEnabled", session.PreviousAutoGameMode) &&
                        RegistryValueWriter.Restore(_registry, RegistryHiveKind.CurrentUser, GameBarKey, "AllowAutoGameMode", session.PreviousAllowAutoGameMode);

                    results.Add(new ActionExecutionResult
                    {
                        ActionId = "gamer.game-mode",
                        Kind = OptimizationActionKind.DisableVisualEffects,
                        TitleKey = "Gamer.Action.GameMode.Title",
                        Success = restored,
                        MessageKey = restored ? "Gamer.Result.GameModeRestored" : "Gamer.Result.GameModeRestoreFailed"
                    });
                }

                if (session.NotificationsPaused)
                {
                    _settings.Current.NotificationsEnabled = session.PreviousNotificationsEnabled;
                    await _settings.SaveAsync(cancellationToken).ConfigureAwait(false);

                    results.Add(new ActionExecutionResult
                    {
                        ActionId = "gamer.notifications",
                        Kind = OptimizationActionKind.SetServiceStartMode,
                        TitleKey = "Gamer.Action.Notifications.Title",
                        Success = true,
                        MessageKey = "Gamer.Result.NotificationsRestored"
                    });
                }
            }
            else
            {
                results.Add(new ActionExecutionResult
                {
                    ActionId = "gamer.keep",
                    Kind = OptimizationActionKind.SetHighPerformanceVisualProfile,
                    TitleKey = "Gamer.Action.Keep.Title",
                    Success = true,
                    MessageKey = "Gamer.Result.Kept"
                });
            }

            _active = null;

            await RecordHistoryAsync(
                "History.Gamer.Stop",
                _localizer.Format("History.Gamer.StopDescription", session.Request.GameName ?? _localizer["Gamer.NoGame"]),
                "History.Gamer.Ok",
                true,
                cancellationToken).ConfigureAwait(false);

            _logger.Info(LogCategory, "Sessao do Modo Gamer encerrada (restaurar: " + restoreEverything + ").");

            return new GameBoostResult
            {
                Success = true,
                Results = results,
                MessageKey = restoreEverything ? "Gamer.Result.Stopped" : "Gamer.Result.StoppedKept"
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private ActionExecutionResult EnableGameMode()
    {
        try
        {
            _registry.SetInt(RegistryHiveKind.CurrentUser, GameBarKey, "AutoGameModeEnabled", 1);
            _registry.SetInt(RegistryHiveKind.CurrentUser, GameBarKey, "AllowAutoGameMode", 1);

            return new ActionExecutionResult
            {
                ActionId = "gamer.game-mode",
                Kind = OptimizationActionKind.SetHighPerformanceVisualProfile,
                TitleKey = "Gamer.Action.GameMode.Title",
                Success = true,
                MessageKey = "Gamer.Result.GameModeEnabled"
            };
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategory, "Nao foi possivel ativar o Modo de Jogo do Windows.", ex);

            return new ActionExecutionResult
            {
                ActionId = "gamer.game-mode",
                Kind = OptimizationActionKind.SetHighPerformanceVisualProfile,
                TitleKey = "Gamer.Action.GameMode.Title",
                MessageKey = "Gamer.Result.GameModeFailed",
                Detail = ex.Message
            };
        }
    }

    private async Task RegisterGameModeRecordsAsync(
        RegistryValueData? autoGameMode,
        RegistryValueData? allowAutoGameMode,
        CancellationToken cancellationToken)
    {
        await RegisterRecordAsync("AutoGameModeEnabled", autoGameMode, cancellationToken).ConfigureAwait(false);
        await RegisterRecordAsync("AllowAutoGameMode", allowAutoGameMode, cancellationToken).ConfigureAwait(false);
    }

    private async Task RegisterRecordAsync(string valueName, RegistryValueData? previous, CancellationToken cancellationToken)
    {
        try
        {
            await _restore.RegisterAsync(
                new RestoreRecord
                {
                    Kind = RestoreRecordKind.RegistryValue,
                    TitleKey = "Restore.Kind.GameMode",
                    Description = _localizer.Format("Restore.GameMode.Description", valueName),
                    TargetPath = GameBarKey + "\\" + valueName,
                    SourceAction = nameof(OptimizationActionKind.SetHighPerformanceVisualProfile),
                    PayloadJson = RestorePayload.ForRegistryBackup(RegistryHiveKind.CurrentUser, GameBarKey, valueName, previous)
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategory, "Nao foi possivel registrar o valor anterior do Modo de Jogo.", ex);
        }
    }

    private async Task<ActionExecutionResult> ApplyPowerPlanAsync(CancellationToken cancellationToken)
    {
        var plans = await _power.GetPlansAsync(cancellationToken).ConfigureAwait(false);
        var plan = plans.FirstOrDefault(item => item.Kind == PowerPlanKind.HighPerformance);

        if (plan is null)
        {
            return Skipped("gamer.power-plan", "Gamer.Action.PowerPlan.Title", "Gamer.Result.PlanUnavailable");
        }

        var outcome = await _power.SetActivePlanAsync(plan, cancellationToken).ConfigureAwait(false);
        return Rebranch(outcome, "gamer.power-plan", OptimizationActionKind.SetPowerPlan, "Gamer.Action.PowerPlan.Title");
    }

    private async Task<ActionExecutionResult> RestorePowerPlanAsync(PowerPlanInfo previous, CancellationToken cancellationToken)
    {
        var plans = await _power.GetPlansAsync(cancellationToken).ConfigureAwait(false);
        var plan = plans.FirstOrDefault(item => item.SchemeGuid == previous.SchemeGuid);

        if (plan is null)
        {
            return Skipped("gamer.power-plan-restore", "Gamer.Action.PowerPlan.Title", "Gamer.Result.PlanUnavailable");
        }

        var outcome = await _power.SetActivePlanAsync(plan, cancellationToken).ConfigureAwait(false);
        return Rebranch(outcome, "gamer.power-plan-restore", OptimizationActionKind.SetPowerPlan, "Gamer.Action.PowerPlan.Title");
    }

    private async Task<ActionExecutionResult> ApplyPriorityAsync(GameBoostSession session, CancellationToken cancellationToken)
    {
        if (session.ProcessId is not int processId)
        {
            return Skipped("gamer.priority", "Gamer.Action.Priority.Title", "Gamer.Result.PriorityNoProcess");
        }

        // A prioridade sobe apenas para o jogo. Nenhum outro processo e rebaixado:
        // rebaixar processos alheios pode travar servicos dos quais o jogo depende.
        var outcome = await _processes.SetPriorityAsync(processId, ProcessPriorityHint.AboveNormal, cancellationToken).ConfigureAwait(false);
        return Rebranch(outcome, "gamer.priority", OptimizationActionKind.CloseProcess, "Gamer.Action.Priority.Title");
    }

    private async Task<ActionExecutionResult> PauseServicesAsync(CancellationToken cancellationToken)
    {
        var services = await _services.GetServicesAsync(cancellationToken).ConfigureAwait(false);
        var stopped = new List<string>();

        foreach (var name in PausableServices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var service = services.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (service is null || service.IsSystemCritical || service.State != WindowsServiceState.Running)
            {
                continue;
            }

            var outcome = await _services.StopAsync(service, cancellationToken).ConfigureAwait(false);
            if (outcome.Success)
            {
                stopped.Add(service.Name);
            }
        }

        if (stopped.Count == 0)
        {
            return Skipped("gamer.services", "Gamer.Action.Services.Title", "Gamer.Result.ServicesNone");
        }

        // O modo de inicio nao e alterado: os servicos voltam no proximo inicio do Windows.
        return new ActionExecutionResult
        {
            ActionId = "gamer.services",
            Kind = OptimizationActionKind.StopWindowsService,
            TitleKey = "Gamer.Action.Services.Title",
            Success = true,
            MessageKey = "Gamer.Result.ServicesStopped",
            Detail = string.Join(", ", stopped)
        };
    }

    private async Task RecordHistoryAsync(
        string actionKey,
        string description,
        string resultKey,
        bool success,
        CancellationToken cancellationToken)
    {
        try
        {
            await _history.RecordAsync(
                new HistoryEntry
                {
                    Category = "History.Category.Gamer",
                    Action = _localizer[actionKey],
                    Description = description,
                    Result = _localizer[resultKey],
                    Success = success
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategory, "Nao foi possivel registrar a sessao do Modo Gamer no historico.", ex);
        }
    }

    private static ActionExecutionResult Skipped(string actionId, string titleKey, string messageKey) => new()
    {
        ActionId = actionId,
        Kind = OptimizationActionKind.SetHighPerformanceVisualProfile,
        TitleKey = titleKey,
        Skipped = true,
        MessageKey = messageKey
    };

    private static ActionExecutionResult Rebranch(
        ActionExecutionResult source,
        string actionId,
        OptimizationActionKind kind,
        string titleKey) => new()
    {
        ActionId = actionId,
        Kind = kind,
        TitleKey = titleKey,
        Success = source.Success,
        Skipped = source.Skipped,
        MessageKey = source.MessageKey,
        Detail = source.Detail,
        BytesFreed = source.BytesFreed,
        Duration = source.Duration,
        RestoreRecordId = source.RestoreRecordId
    };

    private sealed class ActiveSession
    {
        public GameBoostSession Request { get; init; } = new();

        public PowerPlanInfo? PreviousPlan { get; init; }

        public RegistryValueData? PreviousAutoGameMode { get; init; }

        public RegistryValueData? PreviousAllowAutoGameMode { get; init; }

        public bool PreviousNotificationsEnabled { get; init; }

        public bool NotificationsPaused { get; init; }

        public bool VisualEffectsChanged { get; init; }

        public DateTime StartedAtUtc { get; init; }
    }
}
