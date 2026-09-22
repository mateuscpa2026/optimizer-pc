using OptimizerPC.Core;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Storage;

/// <summary>
/// Guarda os registros de reversao criados antes de qualquer alteracao. Eles permitem
/// desfazer mudancas no registro do Windows, na inicializacao e no plano de energia.
/// </summary>
public sealed class RestoreRepository
{
    private const string SelectColumns =
        "Id, Key, Kind, TitleKey, Description, CreatedAtUtc, UndoState, PayloadJson, SourceAction, TargetPath, RestoredAtUtc, RestoreMessage";

    private readonly SqliteDatabase _database;

    public RestoreRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<long> AddAsync(RestoreRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RestoreRecords (Key, Kind, TitleKey, Description, CreatedAtUtc, UndoState, PayloadJson, SourceAction, TargetPath, RestoredAtUtc, RestoreMessage)
            VALUES ($key, $kind, $titleKey, $description, $createdAt, $undoState, $payload, $sourceAction, $targetPath, $restoredAt, $restoreMessage);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$key", record.Key);
        command.Parameters.AddWithValue("$kind", (int)record.Kind);
        command.Parameters.AddWithValue("$titleKey", record.TitleKey);
        command.Parameters.AddWithValue("$description", record.Description);
        command.Parameters.AddWithValue("$createdAt", SqliteMapping.ToDb(record.CreatedAtUtc));
        command.Parameters.AddWithValue("$undoState", (int)record.UndoState);
        command.Parameters.AddWithValue("$payload", record.PayloadJson);
        SqliteMapping.AddNullable(command, "$sourceAction", record.SourceAction);
        SqliteMapping.AddNullable(command, "$targetPath", record.TargetPath);
        command.Parameters.AddWithValue("$restoredAt", record.RestoredAtUtc.HasValue ? SqliteMapping.ToDb(record.RestoredAtUtc.Value) : DBNull.Value);
        SqliteMapping.AddNullable(command, "$restoreMessage", record.RestoreMessage);

        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var id = scalar is long value ? value : 0L;
        record.Id = id;
        return id;
    }

    public async Task<IReadOnlyList<RestoreRecord>> ListAsync(int limit = 200, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {SelectColumns} FROM RestoreRecords ORDER BY CreatedAtUtc DESC, Id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));

        var items = new List<RestoreRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(Read(reader));
        }

        return items;
    }

    public async Task<RestoreRecord?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM RestoreRecords WHERE Key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task UpdateStateAsync(string key, UndoState state, string? message, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE RestoreRecords
            SET UndoState = $state, RestoreMessage = $message, RestoredAtUtc = $restoredAt
            WHERE Key = $key;
            """;
        command.Parameters.AddWithValue("$state", (int)state);
        SqliteMapping.AddNullable(command, "$message", message);
        command.Parameters.AddWithValue("$restoredAt", state == UndoState.Available ? DBNull.Value : SqliteMapping.ToDb(DateTime.UtcNow));
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RestoreRecords;";
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static RestoreRecord Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Key = reader.GetString(1),
        Kind = (RestoreRecordKind)reader.GetInt32(2),
        TitleKey = reader.GetString(3),
        Description = reader.GetString(4),
        CreatedAtUtc = SqliteMapping.FromDb(reader.GetString(5)),
        UndoState = (UndoState)reader.GetInt32(6),
        PayloadJson = reader.GetString(7),
        SourceAction = SqliteMapping.GetNullableString(reader, 8),
        TargetPath = SqliteMapping.GetNullableString(reader, 9),
        RestoredAtUtc = reader.IsDBNull(10) ? null : SqliteMapping.FromDb(reader.GetString(10)),
        RestoreMessage = SqliteMapping.GetNullableString(reader, 11)
    };
}
