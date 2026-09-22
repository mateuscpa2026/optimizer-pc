using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.Services.Storage;

/// <summary>
/// Locais de armazenamento local do aplicativo, sempre sob %LOCALAPPDATA%\OptimizerPC.
/// Nenhum dado e gravado fora dessas pastas, exceto relatorios escolhidos pelo usuario.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    private readonly Lazy<string> _dataFolder;

    public AppPaths(string? dataFolderOverride = null)
    {
        _dataFolder = new Lazy<string>(() =>
        {
            if (!string.IsNullOrWhiteSpace(dataFolderOverride))
            {
                return dataFolderOverride!;
            }

            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local))
            {
                local = Path.Combine(Path.GetTempPath(), "OptimizerPC");
            }

            return Path.Combine(local, "OptimizerPC");
        });
    }

    public string DataFolder => _dataFolder.Value;

    public string LogsFolder => Path.Combine(DataFolder, "Logs");

    public string ReportsFolder => Path.Combine(DataFolder, "Relatorios");

    public string BackupFolder => Path.Combine(DataFolder, "Backups");

    public string DatabasePath => Path.Combine(DataFolder, "optimizerpc.db");

    public string SettingsFilePath => Path.Combine(DataFolder, "settings.json");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(LogsFolder);
        Directory.CreateDirectory(ReportsFolder);
        Directory.CreateDirectory(BackupFolder);
    }
}
