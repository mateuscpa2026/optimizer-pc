using OptimizerPC.Core;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Storage;

/// <summary>Indice dos relatorios gerados, guardado no banco local.</summary>
public sealed class ReportRepository
{
    private const string SelectColumns = "Id, Title, Format, FilePath, CreatedAtUtc, SizeBytes, Summary";

    private readonly SqliteDatabase _database;

    public ReportRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<long> AddAsync(ReportRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Reports (Title, Format, FilePath, CreatedAtUtc, SizeBytes, Summary)
            VALUES ($title, $format, $filePath, $createdAt, $size, $summary);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$title", record.Title);
        command.Parameters.AddWithValue("$format", (int)record.Format);
        command.Parameters.AddWithValue("$filePath", record.FilePath);
        command.Parameters.AddWithValue("$createdAt", SqliteMapping.ToDb(record.CreatedAtUtc));
        command.Parameters.AddWithValue("$size", record.SizeBytes);
        SqliteMapping.AddNullable(command, "$summary", record.Summary);

        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var id = scalar is long value ? value : 0L;
        record.Id = id;
        return id;
    }

    public async Task<IReadOnlyList<ReportRecord>> ListAsync(int limit = 200, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {SelectColumns} FROM Reports ORDER BY CreatedAtUtc DESC, Id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));

        var items = new List<ReportRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new ReportRecord
            {
                Id = reader.GetInt64(0),
                Title = reader.GetString(1),
                Format = (ReportFormat)reader.GetInt32(2),
                FilePath = reader.GetString(3),
                CreatedAtUtc = SqliteMapping.FromDb(reader.GetString(4)),
                SizeBytes = reader.GetInt64(5),
                Summary = SqliteMapping.GetNullableString(reader, 6)
            });
        }

        return items;
    }

    /// <summary>Remove apenas o registro do indice. O arquivo do relatorio nao e apagado.</summary>
    public async Task<bool> RemoveAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Reports WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Reports;";
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
