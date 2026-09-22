using OptimizerPC.Core.Formatting;

namespace OptimizerPC.Core.Models;

public sealed class HistoryEntry
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string Category { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
    public bool Success { get; init; } = true;
    public long BytesFreed { get; init; }
    public string? Details { get; init; }
    public string? RestoreRecordKey { get; init; }

    public string TimestampText => Humanize.Date(TimestampUtc.ToLocalTime());

    public string BytesFreedText => BytesFreed > 0 ? Humanize.Bytes(BytesFreed) : "—";
}

public sealed class LogRecord
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public LogLevel Level { get; init; } = LogLevel.Info;
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Exception { get; init; }

    public string LevelText => Level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARNING",
        _ => "ERROR"
    };

    public string TimestampText => Humanize.Date(TimestampUtc.ToLocalTime());
}

public sealed class ReportRecord
{
    public long Id { get; set; }
    public string Title { get; init; } = string.Empty;
    public ReportFormat Format { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public long SizeBytes { get; init; }
    public string? Summary { get; init; }

    public string CreatedText => Humanize.Date(CreatedAtUtc.ToLocalTime());

    public string SizeText => Humanize.Bytes(SizeBytes);
}

public sealed class ReportRequest
{
    public ReportFormat Format { get; init; } = ReportFormat.Html;
    public bool IncludeSystemInfo { get; init; } = true;
    public bool IncludeDiagnosis { get; init; } = true;
    public bool IncludeRecommendations { get; init; } = true;
    public bool IncludeHistory { get; init; } = true;
    public bool IncludeCleanupSummary { get; init; } = true;
    public string? OutputFolder { get; init; }
    public string? Title { get; init; }
}

public sealed class ReportResult
{
    public bool Success { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public ReportFormat Format { get; init; }
}

/// <summary>
/// Registro de reversao criado antes de alteracoes relevantes (registro do Windows,
/// estado de inicializacao, plano de energia). Permite desfazer a alteracao.
/// </summary>
public sealed class RestoreRecord
{
    public long Id { get; set; }
    public string Key { get; init; } = Guid.NewGuid().ToString("N");
    public RestoreRecordKind Kind { get; init; }
    public string TitleKey { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public UndoState UndoState { get; set; } = UndoState.Available;
    public string PayloadJson { get; init; } = "{}";
    public string? SourceAction { get; init; }
    public string? TargetPath { get; init; }
    public DateTime? RestoredAtUtc { get; set; }
    public string? RestoreMessage { get; set; }

    public string CreatedText => Humanize.Date(CreatedAtUtc.ToLocalTime());

    public bool CanRestore => UndoState == UndoState.Available;
}

public sealed class RestorePointInfo
{
    public int SequenceNumber { get; init; }
    public string Description { get; init; } = string.Empty;
    public DateTime CreationTimeUtc { get; init; }
    public string Type { get; init; } = string.Empty;
    public bool IsSystemRestorePoint { get; init; }

    public string CreatedText => Humanize.Date(CreationTimeUtc.ToLocalTime());
}

public sealed class SystemRestoreStatus
{
    public bool IsSupported { get; init; }
    public bool IsEnabled { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<RestorePointInfo> Points { get; init; } = Array.Empty<RestorePointInfo>();
}

public sealed class AppNotification
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string TitleKey { get; init; } = string.Empty;
    public string MessageKey { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public NotificationSeverity Severity { get; init; } = NotificationSeverity.Info;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
    public string? NavigationTarget { get; init; }
}

public sealed class MaintenanceSchedule
{
    public bool IsEnabled { get; set; }
    public MaintenanceFrequency Frequency { get; set; } = MaintenanceFrequency.Weekly;
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Sunday;
    public int DayOfMonth { get; set; } = 1;
    public string TimeOfDay { get; set; } = "12:00";
    public bool RunDiagnosis { get; set; } = true;
    public bool RunSafeCleanup { get; set; } = true;
    public bool GenerateReport { get; set; } = true;
    public DateTime? LastRunUtc { get; set; }
    public string TaskName { get; set; } = "OptimizerPC-Manutencao";
}

public sealed class AutoOptimizationSelection
{
    public bool CleanTemporaryFiles { get; set; } = true;
    public bool CleanSafeCache { get; set; } = true;
    public bool OptimizeStartup { get; set; }
    public bool AdjustPowerPlan { get; set; }
    public bool FreeUpSpace { get; set; } = true;
    public bool EmptyRecycleBin { get; set; }
}

public sealed class AppSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.PtBr;
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public int MonitoringIntervalMs { get; set; } = 1000;
    public bool ConfirmBeforeActions { get; set; } = true;
    public bool CreateRestorePointBeforeChanges { get; set; } = true;
    public bool ShowTemperatureWhenAvailable { get; set; } = true;
    public string ReportsFolder { get; set; } = string.Empty;
    public string LogsFolder { get; set; } = string.Empty;
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Info;
    public bool PersistLogsInDatabase { get; set; } = true;
    public int LogRetentionDays { get; set; } = 30;
    public bool AutoOptimizationEnabled { get; set; }
    public AutoOptimizationSelection AutoOptimization { get; set; } = new();
    public MaintenanceSchedule Schedule { get; set; } = new();
    public bool HasCompletedFirstRun { get; set; }
    public DateTime? LastDiagnosisUtc { get; set; }
    public DateTime? LastCleanupUtc { get; set; }
    public string? LastHealthSummary { get; set; }
    public bool LowEndModeSuggested { get; set; }
    public bool GamerModeConfigured { get; set; }

    public int MonitoringIntervalOrDefault =>
        MonitoringIntervalMs is 500 or 1000 or 2000 or 5000 ? MonitoringIntervalMs : 1000;
}
