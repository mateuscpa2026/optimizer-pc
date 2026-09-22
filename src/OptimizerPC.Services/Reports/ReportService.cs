using System.Text;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Storage;

namespace OptimizerPC.Services.Reports;

/// <summary>
/// Gera os relatorios locais a partir dos dados reais do computador. O arquivo e
/// escrito apenas na pasta escolhida pelo usuario; o banco guarda so o indice
/// (nome, formato, caminho e tamanho) para a tela de relatorios.
/// </summary>
public sealed class ReportService : IReportService
{
    private const string LogCategory = "Reports";

    private const int MaxIndexRows = 200;

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    private readonly ReportDocumentBuilder _builder;
    private readonly ReportRepository _repository;
    private readonly IHistoryService _history;
    private readonly INotificationService _notifications;
    private readonly ISettingsService _settings;
    private readonly IAppPaths _paths;
    private readonly ILocalizer _localizer;
    private readonly IAppLogger _logger;
    private readonly object _gate = new();

    private IReadOnlyList<ReportRecord> _recent = Array.Empty<ReportRecord>();

    public ReportService(
        ReportDocumentBuilder builder,
        ReportRepository repository,
        IHistoryService history,
        INotificationService notifications,
        ISettingsService settings,
        IAppPaths paths,
        ILocalizer localizer,
        IAppLogger logger)
    {
        _builder = builder;
        _repository = repository;
        _history = history;
        _notifications = notifications;
        _settings = settings;
        _paths = paths;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task<ReportResult> GenerateAsync(ReportRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var document = await _builder.BuildAsync(request, cancellationToken).ConfigureAwait(false);
            var folder = ResolveFolder(request.OutputFolder);
            Directory.CreateDirectory(folder);

            var path = BuildUniquePath(folder, document.BuildFileName(ExtensionFor(request.Format)));
            var size = await WriteFileAsync(path, document, request.Format, cancellationToken).ConfigureAwait(false);

            var record = new ReportRecord
            {
                Title = document.Title,
                Format = request.Format,
                FilePath = path,
                CreatedAtUtc = document.GeneratedAtUtc,
                SizeBytes = size,
                Summary = string.IsNullOrWhiteSpace(document.Summary) ? null : document.Summary
            };

            await IndexAsync(record, cancellationToken).ConfigureAwait(false);
            await RecordHistoryAsync(record, cancellationToken).ConfigureAwait(false);

            var message = _localizer.Format("Report.Message.Created", path);
            PublishNotification(record);

            _logger.Info(LogCategory, "Relatorio " + record.Format + " gerado em " + path + " (" + size + " bytes).");

            return new ReportResult
            {
                Success = true,
                FilePath = path,
                Message = message,
                SizeBytes = size,
                Format = request.Format
            };
        }
        catch (OperationCanceledException)
        {
            _logger.Warning(LogCategory, "Geracao de relatorio cancelada pelo usuario.");
            return new ReportResult
            {
                Success = false,
                Message = _localizer["Report.Message.Cancelled"],
                Format = request.Format
            };
        }
        catch (Exception exception)
        {
            _logger.Error(LogCategory, "Falha ao gerar o relatorio.", exception);
            return new ReportResult
            {
                Success = false,
                Message = _localizer.Format("Report.Message.Failed", exception.Message),
                Format = request.Format
            };
        }
    }

    public IReadOnlyList<ReportRecord> GetRecentReports(int limit = 20)
    {
        lock (_gate)
        {
            return _recent.Take(Math.Max(1, limit)).ToArray();
        }
    }

    public async Task<IReadOnlyList<ReportRecord>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await _repository.ListAsync(MaxIndexRows, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _recent = records;
            }

            return records;
        }
        catch (Exception exception)
        {
            _logger.Error(LogCategory, "Falha ao consultar o indice de relatorios.", exception);

            lock (_gate)
            {
                return _recent;
            }
        }
    }

    /// <summary>
    /// Pasta de destino: a escolha explicita da solicitacao vence; depois a pasta
    /// configurada nas opcoes; por fim a pasta padrao do aplicativo.
    /// </summary>
    private string ResolveFolder(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested) is false)
        {
            return requested!.Trim();
        }

        var configured = _settings.Current.ReportsFolder;
        return string.IsNullOrWhiteSpace(configured) ? _paths.ReportsFolder : configured.Trim();
    }

    private static string ExtensionFor(ReportFormat format) => format switch
    {
        ReportFormat.Txt => ".txt",
        ReportFormat.Pdf => ".pdf",
        ReportFormat.Json => ".json",
        ReportFormat.Csv => ".csv",
        _ => ".html"
    };

    /// <summary>Nunca sobrescreve um relatorio anterior: acrescenta um sufixo numerico.</summary>
    private static string BuildUniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        if (File.Exists(path) is false)
        {
            return path;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(folder, name + "-" + index + extension);
            if (File.Exists(candidate) is false)
            {
                return candidate;
            }
        }

        return Path.Combine(folder, name + "-" + Guid.NewGuid().ToString("N")[..6] + extension);
    }

    private static async Task<long> WriteFileAsync(
        string path,
        ReportDocument document,
        ReportFormat format,
        CancellationToken cancellationToken)
    {
        if (format == ReportFormat.Pdf)
        {
            var bytes = MinimalPdfWriter.Write(document.Title, TextReportWriter.Write(document));
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            return bytes.LongLength;
        }

        var text = format switch
        {
            ReportFormat.Txt => TextReportWriter.Write(document),
            ReportFormat.Json => JsonReportWriter.Write(document),
            ReportFormat.Csv => CsvReportWriter.Write(document),
            _ => HtmlReportWriter.Write(document)
        };

        await File.WriteAllTextAsync(path, text, Utf8WithBom, cancellationToken).ConfigureAwait(false);
        return new FileInfo(path).Length;
    }

    private async Task IndexAsync(ReportRecord record, CancellationToken cancellationToken)
    {
        try
        {
            await _repository.AddAsync(record, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _recent = new[] { record }.Concat(_recent).Take(MaxIndexRows).ToArray();
            }
        }
        catch (Exception exception)
        {
            _logger.Warning(LogCategory, "Relatorio gerado, mas nao foi possivel atualizar o indice local.", exception);
        }
    }

    private async Task RecordHistoryAsync(ReportRecord record, CancellationToken cancellationToken)
    {
        try
        {
            await _history.RecordAsync(
                new HistoryEntry
                {
                    Category = "History.Category.Report",
                    Action = _localizer["Report.History.Created"],
                    Description = record.Title + " · " + _localizer["Report.Format." + record.Format],
                    Result = _localizer.Format("Report.History.Result", record.FilePath),
                    Success = true
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Warning(LogCategory, "Nao foi possivel registrar a geracao do relatorio no historico.", exception);
        }
    }

    private void PublishNotification(ReportRecord record)
    {
        try
        {
            _notifications.Publish(new AppNotification
            {
                TitleKey = "Report.Notification.Title",
                MessageKey = "Report.Notification.Message",
                Detail = record.FilePath,
                Severity = NotificationSeverity.Info,
                NavigationTarget = "Reports"
            });
        }
        catch (Exception exception)
        {
            _logger.Warning(LogCategory, "Nao foi possivel publicar o aviso de relatorio gerado.", exception);
        }
    }
}
