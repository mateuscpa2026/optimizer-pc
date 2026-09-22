using Microsoft.Extensions.DependencyInjection;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Core.Security;
using OptimizerPC.Services.Cleaning;
using OptimizerPC.Services.Configuration;
using OptimizerPC.Services.Diagnostics;
using OptimizerPC.Services.Gaming;
using OptimizerPC.Services.Localization;
using OptimizerPC.Services.Logging;
using OptimizerPC.Services.Notifications;
using OptimizerPC.Services.Optimization;
using OptimizerPC.Services.Reports;
using OptimizerPC.Services.Restore;
using OptimizerPC.Services.Scheduling;
using OptimizerPC.Services.Security;
using OptimizerPC.Services.Storage;
using OptimizerPC.Services.System;

namespace OptimizerPC.Services.DependencyInjection;

/// <summary>
/// Composicao da camada de servicos. Tudo roda em memoria local: o registro nao abre
/// conexao, nao baixa nada e nao altera o sistema. As pastas de dados sao criadas sob
/// demanda no primeiro uso (log, banco ou relatorio).
/// </summary>
public static class OptimizerPcServiceCollectionExtensions
{
    public static IServiceCollection AddOptimizerPcServices(this IServiceCollection services, string? dataFolderOverride = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        RegisterInfrastructure(services, dataFolderOverride);
        RegisterWindowsAccess(services);
        RegisterMonitoring(services);
        RegisterCleaningAndOptimization(services);
        RegisterDiagnostics(services);
        RegisterReports(services);

        return services;
    }

    /// <summary>
    /// Aplica ao log as preferencias salvas. Deve ser chamado na inicializacao e sempre
    /// que as configuracoes mudarem.
    /// </summary>
    public static void ApplyLoggingPreferences(this IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var settings = provider.GetRequiredService<ISettingsService>();
        var sink = provider.GetRequiredService<FileLogSink>();
        sink.ApplyRetention(settings.Current.LogRetentionDays);

        if (provider.GetRequiredService<IAppLogger>() is not AppLogger logger)
        {
            return;
        }

        logger.MinimumLevel = settings.Current.MinimumLogLevel;

        var databaseSink = provider.GetRequiredService<DatabaseLogSink>();
        logger.RemoveSink(databaseSink);
        if (settings.Current.PersistLogsInDatabase)
        {
            logger.AddSink(databaseSink);
        }
    }

    private static void RegisterInfrastructure(IServiceCollection services, string? dataFolderOverride)
    {
        var paths = new AppPaths(dataFolderOverride);
        var clock = new SystemClock();
        var fileSink = new FileLogSink(paths, clock);
        var logger = new AppLogger(LogLevel.Info, fileSink);

        var database = new SqliteDatabase(paths.DatabasePath, logger);
        var historyRepository = new HistoryRepository(database);
        var reportRepository = new ReportRepository(database);
        var restoreRepository = new RestoreRepository(database);
        var logRepository = new LogRepository(database);
        var databaseSink = new DatabaseLogSink(logRepository);

        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton(fileSink);
        services.AddSingleton<IAppLogger>(logger);
        services.AddSingleton(database);
        services.AddSingleton(historyRepository);
        services.AddSingleton(reportRepository);
        services.AddSingleton(restoreRepository);
        services.AddSingleton(logRepository);
        services.AddSingleton(databaseSink);

        services.AddSingleton<JsonLocalizer>();
        services.AddSingleton<ILocalizer>(provider => provider.GetRequiredService<JsonLocalizer>());
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IRegistryService, RegistryService>();
        services.AddSingleton<IElevationService, ElevationService>();
        services.AddSingleton<ICommandExecutionService, CommandExecutionService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IHistoryService, HistoryService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<SafePathValidator>();
    }

    private static void RegisterWindowsAccess(IServiceCollection services)
    {
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        services.AddSingleton<IMetricsProvider, MetricsProvider>();
        services.AddSingleton<IStorageAnalyzer, StorageAnalyzer>();
        services.AddSingleton<IDriveHealthService, DriveHealthService>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IServiceManager, WindowsServiceManager>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IDuplicateFinder, DuplicateFinder>();
        services.AddSingleton<ILargeFileFinder, LargeFileFinder>();
        services.AddSingleton<IPowerService, PowerService>();
        services.AddSingleton<IWindowsToolsService, WindowsToolsService>();
    }

    private static void RegisterMonitoring(IServiceCollection services)
    {
        services.AddSingleton<IMonitoringService, MonitoringService>();
        services.AddSingleton<SystemDataCollector>();
    }

    private static void RegisterCleaningAndOptimization(IServiceCollection services)
    {
        services.AddSingleton<ICleanupScanner, CleanupScanner>();
        services.AddSingleton<ICleanupService, CleanupService>();
        services.AddSingleton<IVisualEffectsService, VisualEffectsService>();
        services.AddSingleton<IRestorePointManager, RestorePointManager>();
        services.AddSingleton<IRestoreService, RestoreService>();
        services.AddSingleton<IHealthScoreService, HealthScoreService>();
        services.AddSingleton<IRecommendationEngine, RecommendationEngine>();
        services.AddSingleton<IOptimizationService, OptimizationService>();
        services.AddSingleton<ILowEndModeService, LowEndModeService>();
        services.AddSingleton<IGameDetector, GameDetector>();
        services.AddSingleton<IGameBoostService, GameBoostService>();
    }

    private static void RegisterDiagnostics(IServiceCollection services)
    {
        services.AddSingleton<IDiagnosticService, DiagnosticService>();
        services.AddSingleton<IDashboardService, DashboardService>();
    }

    private static void RegisterReports(IServiceCollection services)
    {
        services.AddSingleton<ReportDocumentBuilder>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<IMaintenanceScheduler, MaintenanceScheduler>();
    }
}
