using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Interop;

namespace OptimizerPC.Services.System;

/// <summary>
/// Amostras de uso do sistema sem WMI: CPU por GetSystemTimes, memoria por GlobalMemoryStatusEx,
/// disco e rede pelos contadores de desempenho. Quando um contador nao existe no sistema,
/// a amostra marca aquele grupo como indisponivel em vez de exibir um numero inventado.
/// </summary>
public sealed class MetricsProvider : IMetricsProvider, IDisposable
{
    private readonly IAppLogger _logger;
    private readonly object _sync = new();
    private readonly PdhCounterSet? _counters;

    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private bool _hasCpuBaseline;

    private bool _disposed;

    public MetricsProvider(IAppLogger logger)
    {
        _logger = logger;

        try
        {
            _counters = PdhCounterSet.TryCreate();
        }
        catch (Exception exception)
        {
            _logger.Warning("Metrics", "Contadores de desempenho indisponiveis.", exception);
            _counters = null;
        }

        if (_counters is null || !_counters.IsAvailable)
        {
            _logger.Info("Metrics", "Os contadores de disco e rede nao estao disponiveis neste sistema.");
        }
    }

    public Task<MetricSample> SampleAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Sample());
    }

    public void Reset()
    {
        lock (_sync)
        {
            _hasCpuBaseline = false;
        }
    }

    private MetricSample Sample()
    {
        lock (_sync)
        {
            var memory = ReadMemory();
            var cpuPercent = ReadCpuPercent();

            var diskAvailable = false;
            var diskActivity = 0.0;
            var diskReadPerSecond = 0.0;
            var diskWritePerSecond = 0.0;

            var networkAvailable = false;
            var downloadPerSecond = 0.0;
            var uploadPerSecond = 0.0;

            if (_counters is not null && _counters.IsAvailable)
            {
                diskAvailable = _counters.TryReadDiskActivity(out diskActivity);
                var throughputOk = _counters.TryReadDiskThroughput(out diskReadPerSecond, out diskWritePerSecond);
                diskAvailable |= throughputOk;

                networkAvailable = _counters.TryReadNetworkThroughput(out downloadPerSecond, out uploadPerSecond);
            }

            var processCount = 0;
            var threadCount = 0;

            if (_counters is not null && _counters.IsAvailable)
            {
                _counters.TryReadSystemCounts(out processCount, out threadCount);
            }

            return new MetricSample
            {
                TimestampUtc = DateTime.UtcNow,
                CpuPercent = cpuPercent,
                MemoryPercent = memory.LoadPercent,
                MemoryUsedBytes = memory.UsedBytes,
                MemoryTotalBytes = memory.TotalBytes,
                DiskActivityPercent = diskAvailable ? diskActivity : 0,
                DiskReadBytesPerSecond = diskAvailable ? Math.Max(0, diskReadPerSecond) : 0,
                DiskWriteBytesPerSecond = diskAvailable ? Math.Max(0, diskWritePerSecond) : 0,
                NetworkDownloadBytesPerSecond = networkAvailable ? Math.Max(0, downloadPerSecond) : 0,
                NetworkUploadBytesPerSecond = networkAvailable ? Math.Max(0, uploadPerSecond) : 0,
                ProcessCount = processCount,
                ThreadCount = threadCount,
                DiskActivityAvailable = diskAvailable,
                NetworkActivityAvailable = networkAvailable
            };
        }
    }

    private static MemoryInfo ReadMemory()
    {
        var status = MEMORYSTATUSEX.Create();

        if (!NativeKernel.GlobalMemoryStatusEx(ref status))
        {
            return new MemoryInfo();
        }

        var total = (long)status.TotalPhysical;
        var available = (long)status.AvailablePhysical;

        return new MemoryInfo
        {
            TotalBytes = total,
            AvailableBytes = available,
            CommittedBytes = (long)(status.TotalPageFile - status.AvailablePageFile),
            CachedBytes = 0,
            LoadPercent = (int)Math.Clamp(status.MemoryLoad, 0, 100)
        };
    }

    private double ReadCpuPercent()
    {
        if (!NativeKernel.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            _hasCpuBaseline = false;
            return 0;
        }

        var idleTime = idle.ToUInt64();
        var kernelTime = kernel.ToUInt64();
        var userTime = user.ToUInt64();

        if (kernelTime < idleTime)
        {
            return 0;
        }

        // O tempo de kernel ja inclui o tempo ocioso.
        var totalTime = kernelTime + userTime;
        var idleDelta = idleTime - _lastIdle;
        var totalDelta = totalTime - (_lastKernel + _lastUser);

        _lastIdle = idleTime;
        _lastKernel = kernelTime;
        _lastUser = userTime;

        if (!_hasCpuBaseline)
        {
            _hasCpuBaseline = true;
            return 0;
        }

        if (totalDelta == 0 || idleDelta > totalDelta)
        {
            return 0;
        }

        var busy = totalDelta - idleDelta;
        return Math.Clamp(busy * 100.0 / totalDelta, 0, 100);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _counters?.Dispose();
    }
}
