using OptimizerPC.Core.Models;

namespace OptimizerPC.Core.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }

    DateTime LocalNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime LocalNow => DateTime.Now;
}

/// <summary>Log local do aplicativo. Nunca registra senhas, tokens ou conteudo pessoal.</summary>
public interface IAppLogger
{
    void Log(LogLevel level, string category, string message, Exception? exception = null);

    void Debug(string category, string message) => Log(LogLevel.Debug, category, message);

    void Info(string category, string message) => Log(LogLevel.Info, category, message);

    void Warning(string category, string message, Exception? exception = null) =>
        Log(LogLevel.Warning, category, message, exception);

    void Error(string category, string message, Exception? exception = null) =>
        Log(LogLevel.Error, category, message, exception);
}

/// <summary>Locais de armazenamento do aplicativo (dados, logs, relatorios, banco).</summary>
public interface IAppPaths
{
    string DataFolder { get; }

    string LogsFolder { get; }

    string ReportsFolder { get; }

    string BackupFolder { get; }

    string DatabasePath { get; }

    string SettingsFilePath { get; }

    void EnsureCreated();
}

public interface ILocalizer
{
    AppLanguage Current { get; }

    IReadOnlyList<AppLanguage> Available { get; }

    string this[string key] { get; }

    string Format(string key, params object[] args);

    bool TryGet(string key, out string value);

    void SetLanguage(AppLanguage language);

    event EventHandler<AppLanguage>? LanguageChanged;
}

public interface ISettingsService
{
    AppSettings Current { get; }

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    event EventHandler<AppSettings>? SettingsChanged;
}

public sealed record FileEntry(string Path, long Length, DateTime LastWriteTimeUtc);

/// <summary>
/// Acesso a arquivos abstraido para permitir testar a logica de limpeza sem tocar
/// no disco real.
/// </summary>
public interface IFileSystemService
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    IEnumerable<string> EnumerateDirectories(string root);

    IEnumerable<FileEntry> EnumerateFiles(string root, bool recursive);

    IEnumerable<string> EnumerateFilesByPattern(string root, string searchPattern, bool recursive);

    long GetFileSize(string path);

    DateTime GetLastWriteTimeUtc(string path);

    FileAttributes GetAttributes(string path);

    void DeleteFile(string path);

    void DeleteDirectory(string path, bool recursive);

    void CreateDirectory(string path);

    Stream OpenRead(string path);

    long GetAvailableFreeSpace(string path);

    long GetTotalSize(string path);
}

public enum RegistryHiveKind
{
    CurrentUser,
    LocalMachine,
    ClassesRoot,
    Users
}

public sealed record RegistryValueData(string Hive, string SubKey, string? Name, string Kind, string? StringValue, int? IntValue, string[]? MultiStringValue);

public interface IRegistryService
{
    bool KeyExists(RegistryHiveKind hive, string subKey);

    RegistryValueData? GetValue(RegistryHiveKind hive, string subKey, string? name);

    IReadOnlyList<string> GetValueNames(RegistryHiveKind hive, string subKey);

    IReadOnlyList<string> GetSubKeyNames(RegistryHiveKind hive, string subKey);

    string[]? GetMultiString(RegistryHiveKind hive, string subKey, string name);

    void SetString(RegistryHiveKind hive, string subKey, string name, string value);

    void SetExpandString(RegistryHiveKind hive, string subKey, string name, string value);

    void SetInt(RegistryHiveKind hive, string subKey, string name, int value);

    void SetBinary(RegistryHiveKind hive, string subKey, string name, byte[] value);

    void SetMultiString(RegistryHiveKind hive, string subKey, string name, string[] value);

    void DeleteValue(RegistryHiveKind hive, string subKey, string? name);
}

public interface IElevationService
{
    bool IsElevated { get; }

    /// <summary>Reinicia o aplicativo solicitando elevacao via UAC.</summary>
    Task<bool> RestartElevatedAsync(string reasonKey);

    /// <summary>Executa um comando permitido em uma janela elevada (saida visivel ao usuario).</summary>
    Task<bool> RunElevatedCommandAsync(string executablePath, IReadOnlyList<string> arguments, string workingDirectory);
}

public sealed record CommandResult(bool Started, int ExitCode, string StandardOutput, string StandardError, string MessageKey);

public interface ICommandExecutionService
{
    Task<CommandResult> RunAllowedAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

public interface IHistoryService
{
    Task<long> RecordAsync(HistoryEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HistoryEntry>> QueryAsync(int limit = 200, string? category = null, CancellationToken cancellationToken = default);

    Task<int> ClearAsync(CancellationToken cancellationToken = default);

    Task<string?> ExportAsync(string filePath, CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    IReadOnlyList<AppNotification> Recent { get; }

    void Publish(AppNotification notification);

    void MarkAsRead(string id);

    void Clear();

    event EventHandler<AppNotification>? NotificationPublished;

    event EventHandler? RecentChanged;
}
