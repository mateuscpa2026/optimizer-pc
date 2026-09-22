using Microsoft.Data.Sqlite;
using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.Services.Storage;

/// <summary>
/// Banco local SQLite do aplicativo (historico, logs, registros de restauracao e relatorios).
/// Fica em %LOCALAPPDATA%\OptimizerPC e nunca e sincronizado com servidores externos.
/// </summary>
public sealed class SqliteDatabase
{
    private const int SchemaVersion = 1;

    private readonly string _connectionString;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public SqliteDatabase(string databasePath, IAppLogger logger)
    {
        DatabasePath = databasePath;
        _logger = logger;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            var folder = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA synchronous = NORMAL;", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);

            foreach (var statement in SchemaStatements)
            {
                await ExecuteAsync(connection, statement, cancellationToken).ConfigureAwait(false);
            }

            await ExecuteAsync(connection, $"PRAGMA user_version = {SchemaVersion};", cancellationToken).ConfigureAwait(false);
            _initialized = true;
            _logger.Info("Database", "Banco local inicializado na versao de esquema " + SchemaVersion + ".");
        }
        catch (Exception ex)
        {
            _logger.Error("Database", "Falha ao inicializar o banco local.", ex);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>Abre uma conexao pronta para uso, inicializando o esquema na primeira chamada.</summary>
    public async Task<SqliteConnection> OpenReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        return await OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static readonly string[] SchemaStatements =
    {
        """
        CREATE TABLE IF NOT EXISTS History (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            TimestampUtc TEXT NOT NULL,
            Category TEXT NOT NULL,
            Action TEXT NOT NULL,
            Description TEXT NOT NULL,
            Result TEXT NOT NULL,
            Success INTEGER NOT NULL,
            BytesFreed INTEGER NOT NULL DEFAULT 0,
            Details TEXT NULL,
            RestoreRecordKey TEXT NULL
        );
        """,
        "CREATE INDEX IF NOT EXISTS IX_History_TimestampUtc ON History (TimestampUtc DESC);",
        "CREATE INDEX IF NOT EXISTS IX_History_Category ON History (Category);",
        """
        CREATE TABLE IF NOT EXISTS Logs (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            TimestampUtc TEXT NOT NULL,
            Level INTEGER NOT NULL,
            Category TEXT NOT NULL,
            Message TEXT NOT NULL,
            Exception TEXT NULL
        );
        """,
        "CREATE INDEX IF NOT EXISTS IX_Logs_TimestampUtc ON Logs (TimestampUtc DESC);",
        "CREATE INDEX IF NOT EXISTS IX_Logs_Level ON Logs (Level);",
        """
        CREATE TABLE IF NOT EXISTS RestoreRecords (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Key TEXT NOT NULL UNIQUE,
            Kind INTEGER NOT NULL,
            TitleKey TEXT NOT NULL,
            Description TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            UndoState INTEGER NOT NULL,
            PayloadJson TEXT NOT NULL,
            SourceAction TEXT NULL,
            TargetPath TEXT NULL,
            RestoredAtUtc TEXT NULL,
            RestoreMessage TEXT NULL
        );
        """,
        "CREATE INDEX IF NOT EXISTS IX_RestoreRecords_CreatedAtUtc ON RestoreRecords (CreatedAtUtc DESC);",
        """
        CREATE TABLE IF NOT EXISTS Reports (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Title TEXT NOT NULL,
            Format INTEGER NOT NULL,
            FilePath TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            SizeBytes INTEGER NOT NULL DEFAULT 0,
            Summary TEXT NULL
        );
        """,
        "CREATE INDEX IF NOT EXISTS IX_Reports_CreatedAtUtc ON Reports (CreatedAtUtc DESC);"
    };
}
