using System.Text;
using System.Text.Json;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Configuration;

namespace OptimizerPC.Services.Storage;

/// <summary>Historico local das acoes executadas pela ferramenta.</summary>
public sealed class HistoryService : IHistoryService
{
    private const int MaxExportRows = 5000;
    private const char CsvSeparator = ';';

    private readonly HistoryRepository _repository;
    private readonly IAppLogger _logger;
    private readonly ILocalizer _localizer;

    public HistoryService(HistoryRepository repository, IAppLogger logger, ILocalizer localizer)
    {
        _repository = repository;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<long> RecordAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            var id = await _repository.AddAsync(entry, cancellationToken).ConfigureAwait(false);
            _logger.Log(
                entry.Success ? LogLevel.Info : LogLevel.Warning,
                "History",
                entry.Category + "/" + entry.Action + ": " + entry.Result);
            return id;
        }
        catch (Exception ex)
        {
            _logger.Error("History", "Falha ao gravar o historico.", ex);
            return 0;
        }
    }

    public async Task<IReadOnlyList<HistoryEntry>> QueryAsync(int limit = 200, string? category = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.QueryAsync(limit, category, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("History", "Falha ao consultar o historico.", ex);
            return Array.Empty<HistoryEntry>();
        }
    }

    public async Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var removed = await _repository.ClearAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("History", "Historico limpo pelo usuario (" + removed + " registros).");
            return removed;
        }
        catch (Exception ex)
        {
            _logger.Error("History", "Falha ao limpar o historico.", ex);
            return 0;
        }
    }

    /// <summary>Exporta o historico em CSV (padrao) ou JSON, conforme a extensao escolhida.</summary>
    public async Task<string?> ExportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            var entries = await _repository.QueryAsync(MaxExportRows, null, cancellationToken).ConfigureAwait(false);

            var folder = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(folder) is false)
            {
                Directory.CreateDirectory(folder);
            }

            var isJson = string.Equals(Path.GetExtension(filePath), ".json", StringComparison.OrdinalIgnoreCase);
            var content = isJson ? BuildJson(entries) : BuildCsv(entries);

            await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken)
                .ConfigureAwait(false);

            _logger.Info("History", "Historico exportado a pedido do usuario.");
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.Error("History", "Falha ao exportar o historico.", ex);
            return null;
        }
    }

    private string BuildCsv(IReadOnlyList<HistoryEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(CsvSeparator,
            _localizer["History.Column.Timestamp"],
            _localizer["History.Column.Category"],
            _localizer["History.Column.Action"],
            _localizer["History.Column.Description"],
            _localizer["History.Column.Result"],
            _localizer["History.Column.Success"],
            _localizer["History.Column.BytesFreed"]));

        var yes = _localizer["Common.Yes"];
        var no = _localizer["Common.No"];

        foreach (var entry in entries)
        {
            builder.AppendLine(string.Join(CsvSeparator,
                Escape(entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                Escape(entry.Category),
                Escape(entry.Action),
                Escape(entry.Description),
                Escape(entry.Result),
                Escape(entry.Success ? yes : no),
                Escape(entry.BytesFreed.ToString())));
        }

        return builder.ToString();
    }

    private static string BuildJson(IReadOnlyList<HistoryEntry> entries)
    {
        var options = AppJson.CreateOptions(indented: true, skipReadOnlyProperties: false);
        var projection = entries.Select(entry => new
        {
            entry.Id,
            entry.TimestampUtc,
            entry.Category,
            entry.Action,
            entry.Description,
            entry.Result,
            entry.Success,
            entry.BytesFreed,
            entry.Details,
            entry.RestoreRecordKey
        });

        return JsonSerializer.Serialize(projection, options);
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny(new[] { CsvSeparator, '"', '\r', '\n' }) < 0)
        {
            return value;
        }

        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
