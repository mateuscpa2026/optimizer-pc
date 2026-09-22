using Microsoft.Data.Sqlite;
using OptimizerPC.Core;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Storage;

/// <summary>
/// Persistencia dos logs no banco local. As gravacoes sao feitas em lote para nao
/// penalizar a interface, e a retencao e aplicada conforme as configuracoes.
/// </summary>
public sealed class LogRepository
{
    private const string SelectColumns = "Id, TimestampUtc, Level, Category, Message, Exception";

    private readonly SqliteDatabase _database;

    public LogRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task AddRangeAsync(IReadOnlyList<LogRecord> records, CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return;
        }

        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            "INSERT INTO Logs (TimestampUtc, Level, Category, Message, Exception) " +
            "VALUES ($timestamp, $level, $category, $message, $exception);";

        var timestamp = command.Parameters.Add("$timestamp", SqliteType.Text);
        var level = command.Parameters.Add("$level", SqliteType.Integer);
        var category = command.Parameters.Add("$category", SqliteType.Text);
        var message = command.Parameters.Add("$message", SqliteType.Text);
        var exception = command.Parameters.Add("$exception", SqliteType.Text);

        foreach (var record in records)
        {
            timestamp.Value = SqliteMapping.ToDb(record.TimestampUtc);
            level.Value = (int)record.Level;
            category.Value = record.Category;
            message.Value = record.Message;
            exception.Value = (object?)record.Exception ?? DBNull.Value;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LogRecord>> QueryAsync(int limit, LogLevel? minimumLevel = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var filter = minimumLevel.HasValue ? " WHERE Level >= $level" : string.Empty;
        command.CommandText =
            $"SELECT {SelectColumns} FROM Logs{filter} ORDER BY TimestampUtc DESC, Id DESC LIMIT $limit;";

        if (minimumLevel.HasValue)
        {
            command.Parameters.AddWithValue("$level", (int)minimumLevel.Value);
        }

        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 20000));

        var items = new List<LogRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new LogRecord
            {
                Id = reader.GetInt64(0),
                TimestampUtc = SqliteMapping.FromDb(reader.GetString(1)),
                Level = (LogLevel)reader.GetInt32(2),
                Category = reader.GetString(3),
                Message = reader.GetString(4),
                Exception = SqliteMapping.GetNullableString(reader, 5)
            });
        }

        return items;
    }

    /// <summary>Remove registros anteriores ao limite de retencao configurado.</summary>
    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Logs WHERE TimestampUtc < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", SqliteMapping.ToDb(cutoffUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Logs;";
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
