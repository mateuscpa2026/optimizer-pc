using System.Diagnostics;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Interop;

namespace OptimizerPC.Services.System;

/// <summary>
/// Enumeracao de processos com uso de CPU, memoria e entrada/saida em tempo real.
/// Processos essenciais do Windows nunca sao encerrados.
/// </summary>
public sealed class ProcessService : IProcessService, IDisposable
{
    private readonly IAppLogger _logger;
    private readonly object _sync = new();
    private readonly Dictionary<int, ulong> _cpuBaseline = new();
    private readonly Dictionary<int, ulong> _ioBaseline = new();
    private readonly Dictionary<string, FileMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);

    private DateTime _lastSampleUtc = DateTime.UtcNow;
    private bool _disposed;

    public ProcessService(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<ProcessInfoModel>> GetProcessesAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ProcessInfoModel>>(() => Collect(cancellationToken), cancellationToken);

    public Task<ActionExecutionResult> EndProcessAsync(int processId, CancellationToken cancellationToken = default)
        => Task.Run(() => EndProcess(processId), cancellationToken);

    public Task<ActionExecutionResult> SetPriorityAsync(int processId, ProcessPriorityHint priority, CancellationToken cancellationToken = default)
        => Task.Run(() => ApplyPriority(processId, priority), cancellationToken);

    private IReadOnlyList<ProcessInfoModel> Collect(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var processes = new List<ProcessInfoModel>();
        var logicalProcessors = Math.Max(1, Environment.ProcessorCount);

        double elapsedSeconds;
        lock (_sync)
        {
            elapsedSeconds = Math.Max(0.05, (now - _lastSampleUtc).TotalSeconds);
            _lastSampleUtc = now;
        }

        var currentCpu = new Dictionary<int, ulong>();
        var currentIo = new Dictionary<int, ulong>();

        foreach (var process in SafeGetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var id = process.Id;
                var name = process.ProcessName;
                var path = SafeRead(() => process.MainModule?.FileName, null);
                var metadata = ReadMetadata(path);

                if (NativeProcess.TryReadMetrics(id, out var metrics))
                {
                    currentCpu[id] = metrics.CpuTime100Ns;
                    currentIo[id] = metrics.IoTransferBytes;
                }

                var model = new ProcessInfoModel
                {
                    Id = id,
                    Name = name,
                    Description = metadata.Description,
                    Company = metadata.Company,
                    FilePath = path,
                    WorkingSetBytes = SafeRead(() => process.WorkingSet64, 0L),
                    PrivateBytes = SafeRead(() => process.PrivateMemorySize64, 0L),
                    CpuPercent = 0,
                    DiskBytesPerSecond = 0,
                    NetworkBytesPerSecond = 0,
                    ThreadCount = SafeRead(() => process.Threads.Count, 0),
                    HandleCount = SafeRead(() => process.HandleCount, 0),
                    StartTimeUtc = SafeRead<DateTime?>(() => process.StartTime.ToUniversalTime(), null),
                    IsSystemCritical = ProcessSafety.IsCritical(name, id),
                    IsElevatedProcess = false
                };

                processes.Add(model);
            }
            catch (Exception)
            {
                // Processos encerrados durante a leitura sao ignorados.
            }
            finally
            {
                process.Dispose();
            }
        }

        ApplyRates(processes, currentCpu, currentIo, elapsedSeconds, logicalProcessors);

        lock (_sync)
        {
            _cpuBaseline.Clear();
            _ioBaseline.Clear();

            foreach (var pair in currentCpu)
            {
                _cpuBaseline[pair.Key] = pair.Value;
            }

            foreach (var pair in currentIo)
            {
                _ioBaseline[pair.Key] = pair.Value;
            }
        }

        return processes
            .OrderByDescending(p => p.CpuPercent)
            .ThenByDescending(p => p.WorkingSetBytes)
            .ToArray();
    }

    /// <summary>
    /// Calcula a taxa de CPU e de entrada/saida usando a diferenca entre duas coletas.
    /// Sem uma coleta anterior, a taxa permanece zero em vez de exibir um valor estimado.
    /// </summary>
    private void ApplyRates(
        List<ProcessInfoModel> processes,
        Dictionary<int, ulong> currentCpu,
        Dictionary<int, ulong> currentIo,
        double elapsedSeconds,
        int logicalProcessors)
    {
        Dictionary<int, ulong> previousCpu;
        Dictionary<int, ulong> previousIo;

        lock (_sync)
        {
            previousCpu = new Dictionary<int, ulong>(_cpuBaseline);
            previousIo = new Dictionary<int, ulong>(_ioBaseline);
        }

        var hasBaseline = previousCpu.Count > 0;

        foreach (var process in processes)
        {
            if (!hasBaseline || !currentCpu.TryGetValue(process.Id, out var cpuTime) || !previousCpu.TryGetValue(process.Id, out var previousCpuTime))
            {
                continue;
            }

            if (cpuTime <= previousCpuTime)
            {
                continue;
            }

            // 1 tick de GetProcessTimes equivale a 100 ns.
            var cpuSeconds = (cpuTime - previousCpuTime) / 10_000_000.0;
            process.CpuPercent = Math.Clamp(cpuSeconds * 100.0 / elapsedSeconds / logicalProcessors, 0, 100);

            if (currentIo.TryGetValue(process.Id, out var ioBytes) && previousIo.TryGetValue(process.Id, out var previousIoBytes) && ioBytes > previousIoBytes)
            {
                process.DiskBytesPerSecond = (ioBytes - previousIoBytes) / elapsedSeconds;
            }
        }
    }

    /// <summary>
    /// Metadados do executavel sao lidos uma vez por caminho: a leitura de versao e cara
    /// e centenas de processos compartilham os mesmos binarios.
    /// </summary>
    private FileMetadata ReadMetadata(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return default;
        }

        lock (_sync)
        {
            if (_metadataCache.TryGetValue(path, out var cached))
            {
                return cached;
            }
        }

        var metadata = ReadFileMetadata(path);

        lock (_sync)
        {
            _metadataCache[path] = metadata;
        }

        return metadata;
    }

    private static FileMetadata ReadFileMetadata(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return new FileMetadata(
                (info.FileDescription ?? string.Empty).Trim(),
                (info.CompanyName ?? string.Empty).Trim());
        }
        catch (Exception)
        {
            return new FileMetadata(string.Empty, string.Empty);
        }
    }

    private ActionExecutionResult EndProcess(int processId)
    {
        ProcessInfoModel? target = null;

        try
        {
            using var process = Process.GetProcessById(processId);
            target = new ProcessInfoModel { Id = process.Id, Name = process.ProcessName };
        }
        catch (Exception)
        {
            return Failure(processId, "Process.End.NotFound");
        }

        if (ProcessSafety.IsCritical(target))
        {
            _logger.Warning("Process", "Encerramento bloqueado para o processo protegido " + target.Name + " (" + processId + ").");

            return new ActionExecutionResult
            {
                ActionId = "process-" + processId,
                Kind = OptimizationActionKind.CloseProcess,
                TitleKey = "Process.End.Title",
                Success = false,
                Skipped = true,
                MessageKey = "Process.End.Blocked",
                Detail = target.Name,
                Duration = TimeSpan.Zero
            };
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: false);
            process.WaitForExit(5000);

            _logger.Info("Process", "Processo encerrado: " + target.Name + " (" + processId + ").");

            return new ActionExecutionResult
            {
                ActionId = "process-" + processId,
                Kind = OptimizationActionKind.CloseProcess,
                TitleKey = "Process.End.Title",
                Success = true,
                MessageKey = "Process.End.Success",
                Detail = target.Name,
                Duration = TimeSpan.Zero
            };
        }
        catch (Exception exception)
        {
            _logger.Warning("Process", "Nao foi possivel encerrar " + target.Name + " (" + processId + ").", exception);

            return new ActionExecutionResult
            {
                ActionId = "process-" + processId,
                Kind = OptimizationActionKind.CloseProcess,
                TitleKey = "Process.End.Title",
                Success = false,
                MessageKey = "Process.End.Denied",
                Detail = target.Name,
                Duration = TimeSpan.Zero
            };
        }
    }

    private ActionExecutionResult ApplyPriority(int processId, ProcessPriorityHint priority)
    {
        var flag = priority switch
        {
            ProcessPriorityHint.BelowNormal => NativeProcess.BELOW_NORMAL_PRIORITY_CLASS,
            ProcessPriorityHint.AboveNormal => NativeProcess.ABOVE_NORMAL_PRIORITY_CLASS,
            ProcessPriorityHint.High => NativeProcess.HIGH_PRIORITY_CLASS,
            _ => NativeProcess.NORMAL_PRIORITY_CLASS
        };

        if (NativeProcess.TrySetPriority(processId, flag))
        {
            _logger.Info("Process", "Prioridade alterada para " + priority + " no processo " + processId + ".");

            return new ActionExecutionResult
            {
                ActionId = "priority-" + processId,
                Kind = OptimizationActionKind.CloseProcess,
                TitleKey = "Process.Priority.Title",
                Success = true,
                MessageKey = "Process.Priority.Success",
                Duration = TimeSpan.Zero
            };
        }

        return new ActionExecutionResult
        {
            ActionId = "priority-" + processId,
            Kind = OptimizationActionKind.CloseProcess,
            TitleKey = "Process.Priority.Title",
            Success = false,
            MessageKey = "Process.Priority.Denied",
            Duration = TimeSpan.Zero
        };
    }

    private static ActionExecutionResult Failure(int processId, string messageKey) => new()
    {
        ActionId = "process-" + processId,
        Kind = OptimizationActionKind.CloseProcess,
        TitleKey = "Process.End.Title",
        Success = false,
        MessageKey = messageKey,
        Duration = TimeSpan.Zero
    };

    private static IEnumerable<Process> SafeGetProcesses()
    {
        try
        {
            return Process.GetProcesses();
        }
        catch (Exception)
        {
            return Array.Empty<Process>();
        }
    }

    private static T SafeRead<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_sync)
        {
            _cpuBaseline.Clear();
            _ioBaseline.Clear();
            _metadataCache.Clear();
        }
    }

    private readonly record struct FileMetadata(string Description, string Company);
}
