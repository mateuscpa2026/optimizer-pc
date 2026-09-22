using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Configuration;
using OptimizerPC.Tests.Fakes;
using Xunit;

namespace OptimizerPC.Tests.Configuration;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task LoadAsync_SemArquivoUsaPadroesNormalizadosENotifica()
    {
        using var folder = new TempFolder();
        var paths = new TestAppPaths(folder.Root);
        var logger = new FakeLogger();
        var service = new SettingsService(paths, logger);
        AppSettings? changed = null;
        service.SettingsChanged += (_, settings) => changed = settings;

        var settings = await service.LoadAsync();

        Assert.Same(settings, service.Current);
        Assert.Same(settings, changed);
        Assert.Equal(1000, settings.MonitoringIntervalMs);
        Assert.Equal(30, settings.LogRetentionDays);
        Assert.Equal(paths.ReportsFolder, settings.ReportsFolder);
        Assert.Equal(paths.LogsFolder, settings.LogsFolder);
        Assert.Equal("12:00", settings.Schedule.TimeOfDay);
        Assert.Equal("OptimizerPC-Manutencao", settings.Schedule.TaskName);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Info &&
            entry.Category == "Settings" &&
            entry.Message.Contains("Nenhum arquivo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveAsync_NormalizaEPersisteAsConfiguracoes()
    {
        using var folder = new TempFolder();
        var paths = new TestAppPaths(folder.Root);
        var logger = new FakeLogger();
        var service = new SettingsService(paths, logger);
        await service.LoadAsync();

        service.Current.Language = AppLanguage.Es;
        service.Current.MonitoringIntervalMs = 125;
        service.Current.LogRetentionDays = 900;
        service.Current.LogsFolder = " ";
        service.Current.ReportsFolder = " ";
        service.Current.Schedule.DayOfMonth = 31;
        service.Current.Schedule.TimeOfDay = "9:07";
        service.Current.Schedule.TaskName = " ";

        var notificationCount = 0;
        service.SettingsChanged += (_, _) => notificationCount++;
        await service.SaveAsync();

        Assert.True(File.Exists(paths.SettingsFilePath));
        Assert.False(File.Exists(paths.SettingsFilePath + ".tmp"));
        Assert.Equal(1, notificationCount);
        Assert.Equal(1000, service.Current.MonitoringIntervalMs);
        Assert.Equal(365, service.Current.LogRetentionDays);
        Assert.Equal(paths.LogsFolder, service.Current.LogsFolder);
        Assert.Equal(paths.ReportsFolder, service.Current.ReportsFolder);
        Assert.Equal(28, service.Current.Schedule.DayOfMonth);
        Assert.Equal("09:07", service.Current.Schedule.TimeOfDay);
        Assert.Equal("OptimizerPC-Manutencao", service.Current.Schedule.TaskName);

        var reloaded = await new SettingsService(paths, logger).LoadAsync();
        Assert.Equal(AppLanguage.Es, reloaded.Language);
        Assert.Equal(1000, reloaded.MonitoringIntervalMs);
        Assert.Equal(365, reloaded.LogRetentionDays);
        Assert.Equal("09:07", reloaded.Schedule.TimeOfDay);
    }

    [Fact]
    public async Task LoadAsync_ArquivoInvalidoEPreservadoEPadroesSaoRestaurados()
    {
        using var folder = new TempFolder();
        var paths = new TestAppPaths(folder.Root);
        paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.SettingsFilePath, "{ configuracao invalida");
        var logger = new FakeLogger();

        var settings = await new SettingsService(paths, logger).LoadAsync();

        Assert.False(File.Exists(paths.SettingsFilePath));
        Assert.True(File.Exists(paths.SettingsFilePath + ".invalido"));
        Assert.Equal(AppLanguage.PtBr, settings.Language);
        Assert.Equal(paths.ReportsFolder, settings.ReportsFolder);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Category == "Settings" &&
            entry.Message.Contains("invalido", StringComparison.Ordinal));
    }

    private sealed class TestAppPaths : IAppPaths
    {
        public TestAppPaths(string dataFolder)
        {
            DataFolder = dataFolder;
            LogsFolder = Path.Combine(dataFolder, "logs");
            ReportsFolder = Path.Combine(dataFolder, "reports");
            BackupFolder = Path.Combine(dataFolder, "backup");
            DatabasePath = Path.Combine(dataFolder, "optimizerpc.db");
            SettingsFilePath = Path.Combine(dataFolder, "settings.json");
        }

        public string DataFolder { get; }
        public string LogsFolder { get; }
        public string ReportsFolder { get; }
        public string BackupFolder { get; }
        public string DatabasePath { get; }
        public string SettingsFilePath { get; }

        public void EnsureCreated()
        {
            Directory.CreateDirectory(DataFolder);
            Directory.CreateDirectory(LogsFolder);
            Directory.CreateDirectory(ReportsFolder);
            Directory.CreateDirectory(BackupFolder);
        }
    }
}
