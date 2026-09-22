using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.Services;

/// <summary>
/// Execucao sem interface disparada pela tarefa agendada (--maintenance). Somente
/// leitura e limpeza de baixo risco: a limpeza automatica nunca inclui alvos que
/// exigem confirmacao (Lixeira, cache do Windows Update) nem risco medio ou alto.
/// Nada aqui apaga arquivos pessoais ou altera configuracoes do sistema.
/// </summary>
public sealed class MaintenanceRunner
{
    private const string LogCategory = "Maintenance";

    private readonly ISettingsService _settings;
    private readonly IDiagnosticService _diagnostics;
    private readonly ICleanupScanner _scanner;
    private readonly ICleanupService _cleanup;
    private readonly IReportService _reports;
    private readonly IHistoryService _history;
    private readonly INotificationService _notifications;
    private readonly IAppLogger _logger;
    private readonly ILocalizer _localizer;

    public MaintenanceRunner(
        ISettingsService settings,
        IDiagnosticService diagnostics,
        ICleanupScanner scanner,
        ICleanupService cleanup,
        IReportService reports,
        IHistoryService history,
        INotificationService notifications,
        IAppLogger logger,
        ILocalizer localizer)
    {
        _settings = settings;
        _diagnostics = diagnostics;
        _scanner = scanner;
        _cleanup = cleanup;
        _reports = reports;
        _history = history;
        _notifications = notifications;
        _logger = logger;
        _localizer = localizer;
    }

    /// <summary>Codigo de saida para a tarefa agendada: 0 concluido, 1 falha, 2 cancelado.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var schedule = _settings.Current.Schedule;
        _logger.Info(LogCategory, "Manutencao agendada iniciada.");

        var reportPath = string.Empty;
        var freedBytes = 0L;
        var cleanedTargets = 0;

        try
        {
            IReadOnlyList<CleanupTarget> scanned = Array.Empty<CleanupTarget>();

            if (schedule.RunDiagnosis || schedule.RunSafeCleanup)
            {
                scanned = await _scanner
                    .ScanAsync(includeRecycleBin: false, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            if (schedule.RunDiagnosis)
            {
                var result = await _diagnostics
                    .RunAsync(new DiagnosticOptions
                    {
                        IncludeDeepStorageScan = false,
                        IncludeDriveHealth = true,
                        IncludeStartupAnalysis = true,
                        IncludeServiceAnalysis = true,
                        IncludeSecurityChecks = true
                    }, scanned, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (result.WasCancelled)
                {
                    return 2;
                }

                _settings.Current.LastDiagnosisUtc = result.CompletedAtUtc;
            }

            if (schedule.RunSafeCleanup)
            {
                var targets = SelectSafeTargets(scanned);
                if (targets.Count > 0)
                {
                    var summary = await _cleanup
                        .CleanAsync(targets, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (summary.WasCancelled)
                    {
                        return 2;
                    }

                    freedBytes = summary.Results.Sum(item => item.DeletedBytes);
                    cleanedTargets = summary.Results.Count(item => item.Succeeded);

                    if (freedBytes > 0)
                    {
                        _settings.Current.LastCleanupUtc = summary.CompletedAtUtc;
                    }
                }
                else
                {
                    _logger.Info(LogCategory, "Nenhum alvo seguro disponivel para limpeza automatica.");
                }
            }

            if (schedule.GenerateReport)
            {
                var report = await _reports
                    .GenerateAsync(new ReportRequest
                    {
                        Format = ReportFormat.Html,
                        IncludeSystemInfo = true,
                        IncludeDiagnosis = true,
                        IncludeRecommendations = true,
                        IncludeHistory = true,
                        IncludeCleanupSummary = true,
                        Title = _localizer["Report.Title.Maintenance"]
                    }, cancellationToken)
                    .ConfigureAwait(false);

                reportPath = report.Success ? report.FilePath : string.Empty;

                if (report.Success is false)
                {
                    _logger.Warning(LogCategory, "Relatorio de manutencao nao foi gerado: " + report.Message);
                }
            }

            schedule.LastRunUtc = DateTime.UtcNow;
            await SaveSettingsAsync(cancellationToken).ConfigureAwait(false);

            await RecordHistoryAsync(freedBytes, cleanedTargets, reportPath, success: true, cancellationToken)
                .ConfigureAwait(false);

            PublishNotification(success: true, freedBytes, reportPath);

            _logger.Info(LogCategory, "Manutencao agendada concluida.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            _logger.Info(LogCategory, "Manutencao agendada cancelada.");
            return 2;
        }
        catch (Exception exception)
        {
            _logger.Error(LogCategory, "Falha na manutencao agendada.", exception);
            await RecordHistoryAsync(0, 0, string.Empty, success: false, CancellationToken.None).ConfigureAwait(false);
            PublishNotification(success: false, 0, string.Empty);
            return 1;
        }
    }

    /// <summary>
    /// Limpeza automatica restrita a alvos de risco baixo, disponiveis, sem exigencia
    /// de confirmacao e que nao dependam de elevacao — assim a tarefa funciona mesmo
    /// quando o usuario nao autorizou privilegios administrativos.
    /// </summary>
    private static List<CleanupTarget> SelectSafeTargets(IReadOnlyList<CleanupTarget> scanned)
    {
        return scanned
            .Where(target => target.IsAvailable
                && target.RequiresConfirmation is false
                && target.Risk <= RiskLevel.Low
                && target.Elevation != ElevationRequirement.Required
                && target.TotalBytes > 0)
            .Select(target =>
            {
                target.IsSelected = true;
                return target;
            })
            .ToList();
    }

    private async Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _settings.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Warning(LogCategory, "Nao foi possivel gravar as configuracoes apos a manutencao.", exception);
        }
    }

    private async Task RecordHistoryAsync(
        long freedBytes,
        int cleanedTargets,
        string reportPath,
        bool success,
        CancellationToken cancellationToken)
    {
        try
        {
            var description = success
                ? _localizer.Format("Maintenance.History.Detail", cleanedTargets, Humanize.Bytes(freedBytes))
                : _localizer["Maintenance.History.Failed"];

            if (string.IsNullOrWhiteSpace(reportPath) is false)
            {
                description += " · " + reportPath;
            }

            await _history.RecordAsync(new HistoryEntry
            {
                Category = _localizer["Maintenance.History.Category"],
                Action = _localizer["Maintenance.History.Action"],
                Description = description,
                Result = _localizer[success ? "Maintenance.History.Success" : "Maintenance.History.Failure"],
                Success = success,
                BytesFreed = freedBytes,
                Details = string.IsNullOrWhiteSpace(reportPath) ? null : reportPath
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Warning(LogCategory, "Nao foi possivel registrar a manutencao no historico.", exception);
        }
    }

    private void PublishNotification(bool success, long freedBytes, string reportPath)
    {
        try
        {
            if (_settings.Current.NotificationsEnabled is false && success)
            {
                return;
            }

            _notifications.Publish(new AppNotification
            {
                TitleKey = success ? "Notification.Maintenance.Title" : "Notification.Maintenance.ErrorTitle",
                MessageKey = success ? "Notification.Maintenance.Message" : "Notification.Maintenance.ErrorMessage",
                Detail = success
                    ? Humanize.Bytes(freedBytes) +
                      (string.IsNullOrWhiteSpace(reportPath) ? string.Empty : " · " + reportPath)
                    : null,
                Severity = success ? NotificationSeverity.Success : NotificationSeverity.Warning,
                NavigationTarget = success ? "history" : "reports"
            });
        }
        catch (Exception exception)
        {
            _logger.Warning(LogCategory, "Nao foi possivel publicar a notificacao da manutencao.", exception);
        }
    }
}
