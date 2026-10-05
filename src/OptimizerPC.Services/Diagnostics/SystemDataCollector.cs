using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Diagnostics;

/// <summary>
/// Dados do computador coletados uma unica vez e reaproveitados pelo diagnostico e pelo
/// dashboard. As fontes que falharem ficam vazias ou nulas: o aplicativo informa que a
/// leitura nao foi possivel em vez de supor um valor.
/// </summary>
public sealed class SystemData
{
    public SystemSnapshot? Snapshot { get; init; }

    public MetricSample? Metrics { get; init; }

    public IReadOnlyList<VolumeInfo> Volumes { get; init; } = Array.Empty<VolumeInfo>();

    public IReadOnlyList<DeviceHealthReport> DriveHealth { get; init; } = Array.Empty<DeviceHealthReport>();

    public IReadOnlyList<StartupEntry> StartupEntries { get; init; } = Array.Empty<StartupEntry>();

    public IReadOnlyList<WindowsServiceInfo> Services { get; init; } = Array.Empty<WindowsServiceInfo>();

    public IReadOnlyList<ProcessInfoModel> Processes { get; init; } = Array.Empty<ProcessInfoModel>();

    public IReadOnlyList<CleanupTarget> CleanupTargets { get; init; } = Array.Empty<CleanupTarget>();

    public RecycleBinInfo? RecycleBin { get; init; }

    public IReadOnlyList<StorageCategoryUsage> StorageUsage { get; init; } = Array.Empty<StorageCategoryUsage>();

    public long RecoverableBytes => CleanupTargets.Sum(target => target.TotalBytes);

    public VolumeInfo? SystemVolume => Volumes.FirstOrDefault(volume => volume.IsSystemDrive) ?? Volumes.FirstOrDefault();
}

/// <summary>
/// Le cada fonte de dados em um bloco isolado. Uma falha em um sensor nao impede que os
/// demais sejam avaliados, e cada falha fica registrada no log do aplicativo.
/// </summary>
public sealed class SystemDataCollector
{
    // Nenhuma fonte individual pode segurar a coleta para sempre: um disco ou driver
    // que nao responde seria suficiente para travar o painel inteiro.
    private const int SourceTimeoutMs = 10000;

    private readonly ISystemInfoService _systemInfo;
    private readonly IMetricsProvider _metrics;
    private readonly ICleanupScanner _cleanupScanner;
    private readonly IStorageAnalyzer _storageAnalyzer;
    private readonly IDriveHealthService _driveHealth;
    private readonly IStartupService _startup;
    private readonly IServiceManager _services;
    private readonly IProcessService _processes;
    private readonly IAppLogger _logger;

    public SystemDataCollector(
        ISystemInfoService systemInfo,
        IMetricsProvider metrics,
        ICleanupScanner cleanupScanner,
        IStorageAnalyzer storageAnalyzer,
        IDriveHealthService driveHealth,
        IStartupService startup,
        IServiceManager services,
        IProcessService processes,
        IAppLogger logger)
    {
        _systemInfo = systemInfo;
        _metrics = metrics;
        _cleanupScanner = cleanupScanner;
        _storageAnalyzer = storageAnalyzer;
        _driveHealth = driveHealth;
        _startup = startup;
        _services = services;
        _processes = processes;
        _logger = logger;
    }

    public async Task<SystemData> CollectAsync(
        DiagnosticOptions options,
        IReadOnlyList<CleanupTarget>? cleanupTargets,
        CancellationToken cancellationToken)
    {
        var snapshot = await TryAsync(() => _systemInfo.GetSnapshotAsync(cancellationToken), "informacoes do sistema", cancellationToken)
            .ConfigureAwait(false);
        var metrics = await TryAsync(() => _metrics.SampleAsync(cancellationToken), "amostra de uso", cancellationToken)
            .ConfigureAwait(false);

        var volumes = snapshot?.Volumes ?? Array.Empty<VolumeInfo>();
        if (volumes.Count == 0)
        {
            volumes = await TryAsync(() => _storageAnalyzer.GetVolumesAsync(cancellationToken), "volumes", cancellationToken)
                .ConfigureAwait(false) ?? Array.Empty<VolumeInfo>();
        }

        var driveHealth = options.IncludeDriveHealth
            ? await TryAsync(() => _driveHealth.GetHealthReportsAsync(cancellationToken), "saude dos discos", cancellationToken)
                .ConfigureAwait(false) ?? Array.Empty<DeviceHealthReport>()
            : Array.Empty<DeviceHealthReport>();

        var startupEntries = options.IncludeStartupAnalysis
            ? await TryAsync(() => _startup.GetEntriesAsync(cancellationToken), "itens de inicializacao", cancellationToken)
                .ConfigureAwait(false) ?? Array.Empty<StartupEntry>()
            : Array.Empty<StartupEntry>();

        var services = options.IncludeServiceAnalysis
            ? await TryAsync(() => _services.GetServicesAsync(cancellationToken), "servicos do Windows", cancellationToken)
                .ConfigureAwait(false) ?? Array.Empty<WindowsServiceInfo>()
            : Array.Empty<WindowsServiceInfo>();

        var processes = await TryAsync(() => _processes.GetProcessesAsync(cancellationToken), "processos", cancellationToken)
            .ConfigureAwait(false) ?? Array.Empty<ProcessInfoModel>();

        var targets = cleanupTargets;
        if (targets is null)
        {
            targets = await TryAsync(
                    () => _cleanupScanner.ScanAsync(null, true, null, cancellationToken),
                    "varredura de limpeza",
                    cancellationToken)
                .ConfigureAwait(false) ?? Array.Empty<CleanupTarget>();
        }

        var recycleBin = await TryAsync(() => _cleanupScanner.GetRecycleBinInfoAsync(cancellationToken), "lixeira", cancellationToken)
            .ConfigureAwait(false);

        var storageUsage = Array.Empty<StorageCategoryUsage>();
        if (options.IncludeDeepStorageScan)
        {
            var analysis = await TryAsync(() => _storageAnalyzer.AnalyzeAsync(null, cancellationToken), "analise de armazenamento", cancellationToken)
                .ConfigureAwait(false);

            storageUsage = analysis?.Categories.ToArray() ?? Array.Empty<StorageCategoryUsage>();
        }

        return new SystemData
        {
            Snapshot = snapshot,
            Metrics = metrics,
            Volumes = volumes,
            DriveHealth = driveHealth,
            StartupEntries = startupEntries,
            Services = services,
            Processes = processes,
            CleanupTargets = targets,
            RecycleBin = recycleBin,
            StorageUsage = storageUsage
        };
    }

    private async Task<T?> TryAsync<T>(Func<Task<T>> read, string source, CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            var task = read();
            var completed = await Task.WhenAny(task, Task.Delay(SourceTimeoutMs, cancellationToken))
                .ConfigureAwait(false);

            if (completed != task)
            {
                // A leitura ficou pendurada (dispositivo que nao responde). Abandona-a em
                // segundo plano e segue com as demais fontes em vez de travar a coleta.
                _logger.Warning("Diagnostics", "Fonte de dados nao respondeu a tempo: " + source + ".");
                return null;
            }

            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Warning("Diagnostics", "Fonte de dados indisponivel: " + source + ".", exception);
            return null;
        }
    }
}
