using System.Globalization;
using Microsoft.Data.Sqlite;

namespace OptimizerPC.Services.Storage;

/// <summary>
/// Conversao entre os tipos do aplicativo e as colunas do SQLite. Datas sao sempre
/// gravadas em UTC no formato ISO 8601, o que mantem a ordenacao textual correta.
/// </summary>
internal static class SqliteMapping
{
    private const string DateFormat = "o";

    internal static string ToDb(DateTime value) =>
        value.ToUniversalTime().ToString(DateFormat, CultureInfo.InvariantCulture);

    internal static DateTime FromDb(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : DateTime.UtcNow;

    internal static void AddNullable(SqliteCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);

    internal static string? GetNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
