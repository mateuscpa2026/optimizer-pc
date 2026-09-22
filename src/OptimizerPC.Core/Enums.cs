namespace OptimizerPC.Core;

/// <summary>Nivel de risco associado a uma acao de manutencao.</summary>
public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>Impacto esperado de uma acao sobre o desempenho ou espaco em disco.</summary>
public enum ImpactLevel
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>Faixa qualitativa derivada da pontuacao de saude.</summary>
public enum ScoreCategory
{
    Critical = 0,
    Attention = 1,
    Good = 2,
    Excellent = 3
}

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3
}

public enum Severity
{
    Ok = 0,
    Info = 1,
    Warning = 2,
    Critical = 3
}

public enum ThemeMode
{
    Dark = 0,
    Light = 1,
    System = 2
}

public enum AppLanguage
{
    PtBr = 0,
    EnUs = 1,
    Es = 2
}

public enum ActionState
{
    Pending = 0,
    Planned = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Skipped = 5,
    Ignored = 6,
    Undone = 7
}

/// <summary>Categorias de limpeza reconhecidas pelo aplicativo.</summary>
public enum CleanupCategory
{
    UserTemp,
    WindowsTemp,
    ThumbnailCache,
    RecycleBin,
    WindowsErrorReports,
    DeliveryOptimization,
    WindowsUpdateCache,
    PrefetchData,
    CrashDumps,
    ApplicationCaches,
    BrowserCaches,
    InstallerResidue,
    OldLogs,
    FontCache
}

/// <summary>Categorias usadas na analise visual de armazenamento.</summary>
public enum StorageCategory
{
    Applications,
    Documents,
    Downloads,
    Pictures,
    Videos,
    Music,
    Desktop,
    System,
    Temporary,
    Other
}

public enum StartupLocation
{
    CurrentUserRun,
    LocalMachineRun,
    CurrentUserRun32,
    LocalMachineRun32,
    CurrentUserStartupFolder,
    AllUsersStartupFolder,
    CurrentUserStartupApproved,
    LocalMachineStartupApproved
}

public enum StartupImpact
{
    Low = 0,
    Medium = 1,
    High = 2,
    Unknown = 3
}

public enum WindowsServiceState
{
    Unknown = 0,
    Stopped = 1,
    StartPending = 2,
    StopPending = 3,
    Running = 4,
    ContinuePending = 5,
    PausePending = 6,
    Paused = 7
}

public enum ServiceStartMode
{
    Unknown = 0,
    Boot = 1,
    System = 2,
    Automatic = 3,
    Manual = 4,
    Disabled = 5,
    AutomaticDelayed = 6
}

public enum StorageBusType
{
    Unknown = 0,
    Scsi = 1,
    Atapi = 2,
    Ata = 3,
    Ieee1394 = 4,
    Ssa = 5,
    Fibre = 6,
    Usb = 7,
    Raid = 8,
    IScsi = 9,
    Sas = 10,
    Sata = 11,
    Sd = 12,
    Mmc = 13,
    Virtual = 14,
    FileBackedVirtual = 15,
    Spaces = 16,
    Nvme = 17,
    SdUfs = 18,
    Scm = 19,
    Ufs = 20
}

public enum StorageMediaType
{
    Unknown = 0,
    Hdd = 1,
    Ssd = 2,
    ScmOrNvme = 3
}

/// <summary>Tipo de volume tal como o Windows classifica.</summary>
public enum VolumeDriveKind
{
    Unknown = 0,
    Fixed = 1,
    Removable = 2,
    Network = 3,
    CdRom = 4,
    Ram = 5,
    NoRootDirectory = 6
}

public enum DriveHealthStatus
{
    Unknown = 0,
    Healthy = 1,
    Warning = 2,
    Failing = 3,
    Unavailable = 4
}

public enum RecommendationKind
{
    FreeUpSpace,
    CleanTemporaryFiles,
    ReduceStartupPrograms,
    HighMemoryProcess,
    HighCpuProcess,
    ChangePowerPlan,
    LowDiskSpace,
    DisableVisualEffects,
    CheckDiskIntegrity,
    ReviewBackgroundServices,
    EmptyRecycleBin,
    AdjustMonitoring,
    KeepSystemUpdated,
    DefragmentOrTrim
}

public enum OptimizationActionKind
{
    CleanTemporaryFiles,
    EmptyRecycleBin,
    CleanBrowserCache,
    CleanThumbnails,
    CleanOldLogs,
    CleanErrorReports,
    CleanUpdateCache,
    DisableStartupEntry,
    EnableStartupEntry,
    SetPowerPlan,
    DisableVisualEffects,
    EnableVisualEffects,
    SetHighPerformanceVisualProfile,
    TrimVolume,
    FlushDnsCache,
    CloseProcess,
    ExecuteDiagnostic,
    SetServiceStartMode,
    StartWindowsService,
    StopWindowsService
}

public enum RestoreRecordKind
{
    RegistryValue,
    StartupEntryState,
    PowerPlan,
    VisualEffectsProfile,
    FileBackup,
    SystemRestorePoint
}

public enum UndoState
{
    NotSupported = 0,
    Available = 1,
    Restored = 2,
    Failed = 3
}

public enum GameLauncherKind
{
    Unknown = 0,
    Steam = 1,
    Epic = 2,
    GOG = 3,
    Xbox = 4,
    BattleNet = 5,
    Manual = 6
}

public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
    Success = 3
}

public enum MaintenanceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2
}

public enum ReportFormat
{
    Html = 0,
    Txt = 1,
    Pdf = 2,
    Json = 3,
    Csv = 4
}

public enum ElevationRequirement
{
    None = 0,
    Recommended = 1,
    Required = 2
}

/// <summary>Prioridade sugerida para um processo. Valores limitados ao que o Windows expõe com seguranca.</summary>
public enum ProcessPriorityHint
{
    Normal = 0,
    BelowNormal = 1,
    AboveNormal = 2,
    High = 3
}
