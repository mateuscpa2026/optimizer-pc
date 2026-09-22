using Microsoft.Data.Sqlite;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Storage;

/// <summary>Leitura e escrita do historico de acoes no banco local.</summary>
public sealed class HistoryRepository
{
    private const string InsertSql = """
        INSERT INTO History (TimestampUtc, Category, Action, Description, Result, Success, BytesFreed, Details, RestoreRecordKey)
        VALUES ($timestamp, $category, $action, $description, $result, $success, $bytesFreed, $details, $restoreKey);
        SELECT last_insert_rowid();
        """;

    private const string SelectColumns =
        "Id, TimestampUtc, Category, Action, Description, Result, Success, BytesFreed, Details, RestoreRecordKey";

    private readonly SqliteDatabase _database;

    public HistoryRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<long> AddAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = InsertSql;
        command.Parameters.AddWithValue("$timestamp", SqliteMapping.ToDb(entry.TimestampUtc));
        command.Parameters.AddWithValue("$category", entry.Category);
        command.Parameters.AddWithValue("$action", entry.Action);
        command.Parameters.AddWithValue("$description", entry.Description);
        command.Parameters.AddWithValue("$result", entry.Result);
        command.Parameters.AddWithValue("$success", entry.Success ? 1 : 0);
        command.Parameters.AddWithValue("$bytesFreed", entry.BytesFreed);
        SqliteMapping.AddNullable(command, "$details", entry.Details);
        SqliteMapping.AddNullable(command, "$restoreKey", entry.RestoreRecordKey);

        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var id = scalar is long value ? value : 0L;
        entry.Id = id;
        return id;
    }

    public async Task<IReadOnlyList<HistoryEntry>> QueryAsync(int limit, string? category = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var hasCategory = string.IsNullOrWhiteSpace(category) is false;
        var filter = hasCategory ? " WHERE Category = $category" : string.Empty;
        command.CommandText =
            $"SELECT {SelectColumns} FROM History{filter} ORDER BY TimestampUtc DESC, Id DESC LIMIT $limit;";

        if (hasCategory)
        {
            command.Parameters.AddWithValue("$category", category!);
        }

        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));

        var items = new List<HistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(Read(reader));
        }

        return items;
    }

    public async Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM History;";
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static HistoryEntry Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        TimestampUtc = SqliteMapping.FromDb(reader.GetString(1)),
        Category = reader.GetString(2),
        Action = reader.GetString(3),
        Description = reader.GetString(4),
        Result = reader.GetString(5),
        Success = reader.GetInt64(6) != 0,
        BytesFreed = reader.GetInt64(7),
        Details = SqliteMapping.GetNullableString(reader, 8),
        RestoreRecordKey = SqliteMapping.GetNullableString(reader, 9)
    };
}
