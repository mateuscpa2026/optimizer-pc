using OptimizerPC.Core.Models;

namespace OptimizerPC.Core.Abstractions;

/// <summary>
/// Coleta de informações de hardware e sistema operacional.
/// </summary>
public interface ISystemInfoService
{
    Task<SystemSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<OsInfo> GetOsInfoAsync(CancellationToken cancellationToken = default);

    Task<CpuInfo> GetCpuInfoAsync(CancellationToken cancellationToken = default);

    Task<MemoryInfo> GetMemoryInfoAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GpuInfo>> GetGpusAsync(CancellationToken cancellationToken = default);

    Task<MotherboardInfo?> GetMotherboardAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Amostras de uso em tempo real (CPU, memória, disco, rede).
/// </summary>
public interface IMetricsProvider
{
    Task<MetricSample> SampleAsync(CancellationToken cancellationToken = default);

    void Reset();
}

/// <summary>
/// Monitor contínuo que publica amostras periodicamente.
/// </summary>
public interface IMonitoringService : IDisposable
{
    event EventHandler<MetricSample>? SampleAvailable;

    bool IsRunning { get; }

    TimeSpan Interval { get; set; }

    void Start();

    void Stop();

    MetricSample? LastSample { get; }
}

/// <summary>
/// Enumeração e encerramento de processos.
/// </summary>
public interface IProcessService
{
    Task<IReadOnlyList<ProcessInfoModel>> GetProcessesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Encerra um processo. Nunca encerra processos críticos do sistema.
    /// </summary>
    Task<ActionExecutionResult> EndProcessAsync(int processId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Define a prioridade de um processo. Valores suportados conforme política do sistema.
    /// </summary>
    Task<ActionExecutionResult> SetPriorityAsync(int processId, ProcessPriorityHint priority, CancellationToken cancellationToken = default);
}

/// <summary>
/// Itens de inicialização do Windows.
/// </summary>
public interface IStartupService
{
    Task<IReadOnlyList<StartupEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Habilita ou desabilita um item de inicialização. A alteração é reversível.
    /// </summary>
    Task<ActionExecutionResult> SetEnabledAsync(StartupEntry entry, bool enabled, CancellationToken cancellationToken = default);
}

/// <summary>
/// Serviços do Windows (leitura e controle de inicialização).
/// </summary>
public interface IServiceManager
{
    Task<IReadOnlyList<WindowsServiceInfo>> GetServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Altera o modo de inicialização de um serviço. Serviços críticos são bloqueados.
    /// </summary>
    Task<ActionExecutionResult> SetStartModeAsync(WindowsServiceInfo service, ServiceStartMode startMode, CancellationToken cancellationToken = default);

    Task<ActionExecutionResult> StartAsync(WindowsServiceInfo service, CancellationToken cancellationToken = default);

    Task<ActionExecutionResult> StopAsync(WindowsServiceInfo service, CancellationToken cancellationToken = default);
}

/// <summary>
/// Planos de energia do Windows.
/// </summary>
public interface IPowerService
{
    Task<IReadOnlyList<PowerPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<PowerPlanInfo?> GetActivePlanAsync(CancellationToken cancellationToken = default);

    Task<ActionExecutionResult> SetActivePlanAsync(PowerPlanInfo plan, CancellationToken cancellationToken = default);
}

/// <summary>
/// Análise de armazenamento, arquivos grandes e duplicados.
/// </summary>
public interface IStorageAnalyzer
{
    Task<StorageAnalysisResult> AnalyzeAsync(IProgress<StorageScanProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VolumeInfo>> GetVolumesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StorageDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Progresso da varredura de armazenamento.
/// </summary>
/// <param name="CurrentPath">Pasta sendo analisada no momento.</param>
/// <param name="FilesProcessed">Quantidade de arquivos lidos.</param>
/// <param name="BytesProcessed">Bytes somados até o momento.</param>
/// <param name="PercentComplete">Percentual estimado da varredura (0 a 100).</param>
public readonly record struct StorageScanProgress(string CurrentPath, int FilesProcessed, long BytesProcessed, double PercentComplete);

/// <summary>
/// Saúde dos discos (SMART quando o dispositivo suportar).
/// </summary>
public interface IDriveHealthService
{
    Task<IReadOnlyList<DeviceHealthReport>> GetHealthReportsAsync(CancellationToken cancellationToken = default);

    Task<VolumeMaintenanceResult> RunTrimAsync(VolumeInfo volume, CancellationToken cancellationToken = default);

    Task<VolumeMaintenanceResult> RunChkdskScanAsync(VolumeInfo volume, CancellationToken cancellationToken = default);
}

/// <summary>
/// Relatório de saúde de um dispositivo de armazenamento.
/// </summary>
public sealed class DeviceHealthReport
{
    public string DeviceName { get; init; } = string.Empty;

    public string? Model { get; init; }

    public StorageMediaType MediaType { get; init; } = StorageMediaType.Unknown;

    public StorageBusType BusType { get; init; } = StorageBusType.Unknown;

    public DriveHealthStatus Status { get; init; } = DriveHealthStatus.Unknown;

    public int? TemperatureCelsius { get; init; }

    public long? PowerOnHours { get; init; }

    public long? TotalBytesWritten { get; init; }

    public int? WearPercent { get; init; }

    public bool SmartAvailable { get; init; }

    /// <summary>Chave de localização explicando a origem dos dados.</summary>
    public string SourceKey { get; init; } = "Health.Source.NotAvailable";

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Resultado de uma operação de manutenção em volume.
/// </summary>
public sealed class VolumeMaintenanceResult
{
    public bool Started { get; init; }

    public int ExitCode { get; init; }

    public string Output { get; init; } = string.Empty;

    public string MessageKey { get; init; } = string.Empty;

    public bool RequiresElevation { get; init; }
}

/// <summary>
/// Localização de arquivos grandes.
/// </summary>
public interface ILargeFileFinder
{
    Task<IReadOnlyList<LargeFileInfo>> FindAsync(
        IReadOnlyList<string> roots,
        long minimumSize,
        IProgress<StorageScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Localização de arquivos duplicados por conteúdo.
/// </summary>
public interface IDuplicateFinder
{
    Task<DuplicateScanResult> FindAsync(
        IReadOnlyList<string> roots,
        long minimumSize,
        IProgress<DuplicateScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Envio de arquivos para a Lixeira do Windows. A operação é sempre reversível
/// e nunca exclui permanentemente: cada caminho passa por validação de segurança
/// antes de qualquer alteração no disco.
/// </summary>
public interface IRecycleBinMover
{
    Task<FileMoveResult> MoveToRecycleBinAsync(
        IReadOnlyList<string> paths,
        IReadOnlyList<string> allowedRoots,
        IProgress<StorageScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Ferramentas nativas do Windows disponíveis na tela "Ferramentas".
/// </summary>
public interface IWindowsToolsService
{
    IReadOnlyList<WindowsToolDescriptor> GetTools();

    Task<CommandResult> RunAsync(string toolId, IEnumerable<string>? extraArguments = null, IProgress<string>? output = null, CancellationToken cancellationToken = default);
}
