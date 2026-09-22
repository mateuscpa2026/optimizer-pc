using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Core.Security;

namespace OptimizerPC.Tests.Fakes;

internal sealed record LoggedEntry(LogLevel Level, string Category, string Message, Exception? Exception);

/// <summary>Log em memoria. Permite verificar o que o codigo registrou sem tocar o disco.</summary>
internal sealed class FakeLogger : IAppLogger
{
    private readonly List<LoggedEntry> _entries = new();
    private readonly object _gate = new();

    public IReadOnlyList<LoggedEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToList();
            }
        }
    }

    public bool Contains(LogLevel level, string category) =>
        Entries.Any(entry => entry.Level == level && string.Equals(entry.Category, category, StringComparison.Ordinal));

    public void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        lock (_gate)
        {
            _entries.Add(new LoggedEntry(level, category, message, exception));
        }
    }
}

/// <summary>Relogio deterministico.</summary>
internal sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public DateTime LocalNow { get; set; } = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Local);
}

internal sealed class FakeElevationService : IElevationService
{
    public bool IsElevated { get; set; }

    public int RestartElevatedCalls { get; private set; }

    public List<string> ElevatedCommands { get; } = new();

    public Task<bool> RestartElevatedAsync(string reasonKey)
    {
        RestartElevatedCalls++;
        return Task.FromResult(false);
    }

    public Task<bool> RunElevatedCommandAsync(string executablePath, IReadOnlyList<string> arguments, string workingDirectory)
    {
        ElevatedCommands.Add(executablePath + " " + string.Join(' ', arguments));
        return Task.FromResult(false);
    }
}

internal sealed class FakeSettingsService : ISettingsService
{
    public AppSettings Current { get; set; } = new();

    public int SaveCount { get; private set; }

    public event EventHandler<AppSettings>? SettingsChanged;

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        SettingsChanged?.Invoke(this, Current);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Localizador de teste: devolve a propria chave, o que permite fixar o contrato de
/// cada mensagem sem depender do idioma instalado na maquina.
/// </summary>
internal sealed class FakeLocalizer : ILocalizer
{
    public AppLanguage Current { get; private set; } = AppLanguage.PtBr;

    public IReadOnlyList<AppLanguage> Available { get; } = new[] { AppLanguage.PtBr, AppLanguage.EnUs, AppLanguage.Es };

    public event EventHandler<AppLanguage>? LanguageChanged;

    public string this[string key] => key;

    public string Format(string key, params object[] args) =>
        args.Length == 0 ? key : key + "(" + string.Join(", ", args) + ")";

    public bool TryGet(string key, out string value)
    {
        value = key;
        return true;
    }

    public void SetLanguage(AppLanguage language)
    {
        Current = language;
        LanguageChanged?.Invoke(this, language);
    }
}

internal sealed class FakeHistoryService : IHistoryService
{
    public List<HistoryEntry> Entries { get; } = new();

    public Task<long> RecordAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.FromResult((long)Entries.Count);
    }

    public Task<IReadOnlyList<HistoryEntry>> QueryAsync(int limit = 200, string? category = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<HistoryEntry> result = Entries
            .Where(entry => category is null || string.Equals(entry.Category, category, StringComparison.Ordinal))
            .Take(limit)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        var count = Entries.Count;
        Entries.Clear();
        return Task.FromResult(count);
    }

    public Task<string?> ExportAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(filePath);
}

/// <summary>
/// Registro em memoria que tambem contabiliza escritas. Usado nos testes de seguranca para
/// provar que as rotinas de leitura (diagnostico, varredura) nunca gravam no registro.
/// </summary>
internal sealed class FakeRegistryService : IRegistryService
{
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RegistryValueData> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _writes = new();

    public IReadOnlyList<string> Writes => _writes;

    public bool HasWrites => _writes.Count > 0;

    public void SeedKey(RegistryHiveKind hive, string subKey) => _keys.Add(KeyOf(hive, subKey));

    public void SeedInt(RegistryHiveKind hive, string subKey, string name, int value) =>
        Seed(hive, subKey, name, new RegistryValueData(hive.ToString(), subKey, name, "DWord", null, value, null));

    public void SeedString(RegistryHiveKind hive, string subKey, string name, string value) =>
        Seed(hive, subKey, name, new RegistryValueData(hive.ToString(), subKey, name, "String", value, null, null));

    public bool KeyExists(RegistryHiveKind hive, string subKey) => _keys.Contains(KeyOf(hive, subKey));

    public RegistryValueData? GetValue(RegistryHiveKind hive, string subKey, string? name) =>
        _values.TryGetValue(KeyOf(hive, subKey, name), out var value) ? value : null;

    public IReadOnlyList<string> GetValueNames(RegistryHiveKind hive, string subKey)
    {
        var prefix = KeyOf(hive, subKey) + "\\";
        return _values.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(key => key[prefix.Length..])
            .Where(name => name.Length > 0)
            .ToList();
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHiveKind hive, string subKey) => Array.Empty<string>();

    public string[]? GetMultiString(RegistryHiveKind hive, string subKey, string name) =>
        GetValue(hive, subKey, name)?.MultiStringValue;

    public void SetString(RegistryHiveKind hive, string subKey, string name, string value)
    {
        _keys.Add(KeyOf(hive, subKey));
        _values[KeyOf(hive, subKey, name)] = new RegistryValueData(hive.ToString(), subKey, name, "String", value, null, null);
        _writes.Add("SetString " + KeyOf(hive, subKey, name));
    }

    public void SetExpandString(RegistryHiveKind hive, string subKey, string name, string value)
    {
        _keys.Add(KeyOf(hive, subKey));
        _values[KeyOf(hive, subKey, name)] = new RegistryValueData(hive.ToString(), subKey, name, "ExpandString", value, null, null);
        _writes.Add("SetExpandString " + KeyOf(hive, subKey, name));
    }

    public void SetInt(RegistryHiveKind hive, string subKey, string name, int value)
    {
        _keys.Add(KeyOf(hive, subKey));
        _values[KeyOf(hive, subKey, name)] = new RegistryValueData(hive.ToString(), subKey, name, "DWord", null, value, null);
        _writes.Add("SetInt " + KeyOf(hive, subKey, name));
    }

    public void SetBinary(RegistryHiveKind hive, string subKey, string name, byte[] value)
    {
        _keys.Add(KeyOf(hive, subKey));
        _values[KeyOf(hive, subKey, name)] = new RegistryValueData(hive.ToString(), subKey, name, "Binary", Convert.ToBase64String(value), null, null);
        _writes.Add("SetBinary " + KeyOf(hive, subKey, name));
    }

    public void SetMultiString(RegistryHiveKind hive, string subKey, string name, string[] value)
    {
        _keys.Add(KeyOf(hive, subKey));
        _values[KeyOf(hive, subKey, name)] = new RegistryValueData(hive.ToString(), subKey, name, "MultiString", null, null, value);
        _writes.Add("SetMultiString " + KeyOf(hive, subKey, name));
    }

    public void DeleteValue(RegistryHiveKind hive, string subKey, string? name)
    {
        _values.Remove(KeyOf(hive, subKey, name));
        _writes.Add("DeleteValue " + KeyOf(hive, subKey, name));
    }

    private void Seed(RegistryHiveKind hive, string subKey, string name, RegistryValueData value)
    {
        _keys.Add(KeyOf(hive, subKey));
        _values[KeyOf(hive, subKey, name)] = value;
    }

    private static string KeyOf(RegistryHiveKind hive, string subKey, string? name = null) =>
        hive + "\\" + subKey + (name is null ? string.Empty : "\\" + name);
}

/// <summary>
/// Sistema de arquivos em memoria. Nada toca o disco real: os testes de limpeza
/// provam o comportamento sem risco de apagar arquivos da maquina de desenvolvimento.
/// </summary>
internal sealed class InMemoryFileSystem : IFileSystemService
{
    private sealed record FakeFile(long Length, DateTime LastWriteTimeUtc);

    private readonly Dictionary<string, FakeFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public List<string> DeletedFiles { get; } = new();

    public List<string> DeletedDirectories { get; } = new();

    /// <summary>Caminhos cuja remocao deve falhar, para exercitar o tratamento de erro.</summary>
    public HashSet<string> FailingPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pastas marcadas como reparse point (links). Nunca devem ser seguidas.</summary>
    public HashSet<string> ReparsePoints { get; } = new(StringComparer.OrdinalIgnoreCase);

    public long AvailableFreeSpace { get; set; } = 500L * 1024 * 1024 * 1024;

    public long TotalSize { get; set; } = 1024L * 1024 * 1024 * 1024;

    public InMemoryFileSystem AddDirectory(string path)
    {
        var normalized = ProtectedPaths.Normalize(path);
        if (normalized.Length == 0)
        {
            return this;
        }

        _directories.Add(normalized);

        var parent = Path.GetDirectoryName(normalized);
        while (string.IsNullOrEmpty(parent) is false)
        {
            _directories.Add(parent);
            parent = Path.GetDirectoryName(parent);
        }

        return this;
    }

    public InMemoryFileSystem AddFile(string path, long length, DateTime? lastWriteUtc = null)
    {
        var normalized = ProtectedPaths.Normalize(path);
        var parent = Path.GetDirectoryName(normalized);
        if (string.IsNullOrEmpty(parent) is false)
        {
            AddDirectory(parent);
        }

        _files[normalized] = new FakeFile(length, lastWriteUtc ?? DateTime.UtcNow.AddDays(-30));
        return this;
    }

    public bool DirectoryExists(string path) => _directories.Contains(ProtectedPaths.Normalize(path));

    public bool FileExists(string path) => _files.ContainsKey(ProtectedPaths.Normalize(path));

    public IEnumerable<string> EnumerateDirectories(string root)
    {
        var normalized = ProtectedPaths.Normalize(root);

        return _directories
            .Where(directory => string.Equals(Path.GetDirectoryName(directory), normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IEnumerable<FileEntry> EnumerateFiles(string root, bool recursive)
    {
        var normalized = ProtectedPaths.Normalize(root);
        var result = new List<FileEntry>();

        foreach (var (path, file) in _files)
        {
            if (ProtectedPaths.IsUnder(path, normalized) is false)
            {
                continue;
            }

            if (recursive is false &&
                string.Equals(Path.GetDirectoryName(path), normalized, StringComparison.OrdinalIgnoreCase) is false)
            {
                continue;
            }

            result.Add(new FileEntry(path, file.Length, file.LastWriteTimeUtc));
        }

        return result.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IEnumerable<string> EnumerateFilesByPattern(string root, string searchPattern, bool recursive) =>
        EnumerateFiles(root, recursive)
            .Where(entry => MatchesPattern(Path.GetFileName(entry.Path), searchPattern))
            .Select(entry => entry.Path)
            .ToList();

    public long GetFileSize(string path) =>
        _files.TryGetValue(ProtectedPaths.Normalize(path), out var file) ? file.Length : 0;

    public DateTime GetLastWriteTimeUtc(string path) =>
        _files.TryGetValue(ProtectedPaths.Normalize(path), out var file) ? file.LastWriteTimeUtc : DateTime.MinValue;

    public FileAttributes GetAttributes(string path)
    {
        var normalized = ProtectedPaths.Normalize(path);

        if (_files.ContainsKey(normalized))
        {
            return FileAttributes.Normal;
        }

        if (_directories.Contains(normalized))
        {
            return ReparsePoints.Contains(normalized)
                ? FileAttributes.Directory | FileAttributes.ReparsePoint
                : FileAttributes.Directory;
        }

        throw new FileNotFoundException("Caminho nao registrado no sistema de arquivos de teste.", normalized);
    }

    public void DeleteFile(string path)
    {
        var normalized = ProtectedPaths.Normalize(path);

        if (FailingPaths.Contains(normalized))
        {
            throw new IOException("Falha simulada ao remover " + normalized + ".");
        }

        if (_files.Remove(normalized) is false)
        {
            throw new FileNotFoundException("Arquivo nao encontrado.", normalized);
        }

        DeletedFiles.Add(normalized);
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        var normalized = ProtectedPaths.Normalize(path);

        if (FailingPaths.Contains(normalized))
        {
            throw new IOException("Falha simulada ao remover " + normalized + ".");
        }

        if (_directories.Contains(normalized) is false)
        {
            throw new DirectoryNotFoundException(normalized);
        }

        if (recursive)
        {
            foreach (var file in _files.Keys.Where(entry => ProtectedPaths.IsUnder(entry, normalized)).ToList())
            {
                _files.Remove(file);
            }

            foreach (var directory in _directories.Where(entry => ProtectedPaths.IsUnder(entry, normalized)).ToList())
            {
                _directories.Remove(directory);
            }
        }

        _directories.Remove(normalized);
        DeletedDirectories.Add(normalized);
    }

    public void CreateDirectory(string path) => AddDirectory(path);

    public Stream OpenRead(string path) => throw new NotSupportedException("Leitura de conteudo nao e usada nos testes.");

    public long GetAvailableFreeSpace(string path) => AvailableFreeSpace;

    public long GetTotalSize(string path) => TotalSize;

    /// <summary>Mesma semantica do filtro de producao: no maximo um asterisco.</summary>
    internal static bool MatchesPattern(string name, string pattern)
    {
        var star = pattern.IndexOf('*');
        if (star < 0)
        {
            return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
        }

        var prefix = pattern[..star];
        var suffix = pattern[(star + 1)..];

        return name.Length >= prefix.Length + suffix.Length
               && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Pasta temporaria de teste, sempre removida ao final.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder(string prefix = "OptimizerPC-Tests")
    {
        Root = Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Combine(string relative) => Path.Combine(Root, relative);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception)
        {
            // Pasta temporaria presa por outro processo: o sistema operacional limpa depois.
        }
    }
}

internal sealed class StubSystemInfoService : ISystemInfoService
{
    public SystemSnapshot Snapshot { get; set; } = new();

    public OsInfo Os { get; set; } = new();

    public CpuInfo Cpu { get; set; } = new();

    public MemoryInfo Memory { get; set; } = new();

    public IReadOnlyList<GpuInfo> Gpus { get; set; } = Array.Empty<GpuInfo>();

    public MotherboardInfo? Motherboard { get; set; }

    public Task<SystemSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);

    public Task<OsInfo> GetOsInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(Os);

    public Task<CpuInfo> GetCpuInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(Cpu);

    public Task<MemoryInfo> GetMemoryInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(Memory);

    public Task<IReadOnlyList<GpuInfo>> GetGpusAsync(CancellationToken cancellationToken = default) => Task.FromResult(Gpus);

    public Task<MotherboardInfo?> GetMotherboardAsync(CancellationToken cancellationToken = default) => Task.FromResult(Motherboard);
}

internal sealed class StubMetricsProvider : IMetricsProvider
{
    public MetricSample Sample { get; set; } = new();

    public int ResetCount { get; private set; }

    public Task<MetricSample> SampleAsync(CancellationToken cancellationToken = default) => Task.FromResult(Sample);

    public void Reset() => ResetCount++;
}

internal sealed class StubStorageAnalyzer : IStorageAnalyzer
{
    public StorageAnalysisResult Analysis { get; set; } = new();

    public IReadOnlyList<VolumeInfo> Volumes { get; set; } = Array.Empty<VolumeInfo>();

    public IReadOnlyList<StorageDeviceInfo> Devices { get; set; } = Array.Empty<StorageDeviceInfo>();

    public Task<StorageAnalysisResult> AnalyzeAsync(IProgress<StorageScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Analysis);

    public Task<IReadOnlyList<VolumeInfo>> GetVolumesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Volumes);

    public Task<IReadOnlyList<StorageDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Devices);
}

internal sealed class StubDriveHealthService : IDriveHealthService
{
    public IReadOnlyList<DeviceHealthReport> Reports { get; set; } = Array.Empty<DeviceHealthReport>();

    public Task<IReadOnlyList<DeviceHealthReport>> GetHealthReportsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Reports);

    public Task<VolumeMaintenanceResult> RunTrimAsync(VolumeInfo volume, CancellationToken cancellationToken = default) =>
        Task.FromResult(new VolumeMaintenanceResult());

    public Task<VolumeMaintenanceResult> RunChkdskScanAsync(VolumeInfo volume, CancellationToken cancellationToken = default) =>
        Task.FromResult(new VolumeMaintenanceResult());
}

internal sealed class StubStartupService : IStartupService
{
    public IReadOnlyList<StartupEntry> Entries { get; set; } = Array.Empty<StartupEntry>();

    public Task<IReadOnlyList<StartupEntry>> GetEntriesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Entries);

    public Task<ActionExecutionResult> SetEnabledAsync(StartupEntry entry, bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ActionExecutionResult
        {
            ActionId = entry.Id,
            Success = true,
            MessageKey = "Action.State.Succeeded"
        });
}

internal sealed class StubServiceManager : IServiceManager
{
    public IReadOnlyList<WindowsServiceInfo> Services { get; set; } = Array.Empty<WindowsServiceInfo>();

    public Task<IReadOnlyList<WindowsServiceInfo>> GetServicesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Services);

    public Task<ActionExecutionResult> SetStartModeAsync(WindowsServiceInfo service, ServiceStartMode startMode, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result(service, "Action.State.Succeeded"));

    public Task<ActionExecutionResult> StartAsync(WindowsServiceInfo service, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result(service, "Action.State.Succeeded"));

    public Task<ActionExecutionResult> StopAsync(WindowsServiceInfo service, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result(service, "Action.State.Succeeded"));

    private static ActionExecutionResult Result(WindowsServiceInfo service, string messageKey) => new()
    {
        ActionId = service.Name,
        Success = true,
        MessageKey = messageKey
    };
}

internal sealed class StubProcessService : IProcessService
{
    public IReadOnlyList<ProcessInfoModel> Processes { get; set; } = Array.Empty<ProcessInfoModel>();

    public Task<IReadOnlyList<ProcessInfoModel>> GetProcessesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Processes);

    public Task<ActionExecutionResult> EndProcessAsync(int processId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result(processId));

    public Task<ActionExecutionResult> SetPriorityAsync(int processId, ProcessPriorityHint priority, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result(processId));

    private static ActionExecutionResult Result(int processId) => new()
    {
        ActionId = processId.ToString(),
        Success = true,
        MessageKey = "Action.State.Succeeded"
    };
}

internal sealed class RecordingCleanupScanner : ICleanupScanner
{
    public IReadOnlyList<CleanupTarget> Targets { get; set; } = Array.Empty<CleanupTarget>();

    public int ScanCount { get; private set; }

    public Task<IReadOnlyList<CleanupTarget>> ScanAsync(
        IReadOnlyList<CleanupCategory>? categories = null,
        bool includeRecycleBin = true,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ScanCount++;
        return Task.FromResult(Targets);
    }

    public Task<RecycleBinInfo> GetRecycleBinInfoAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new RecycleBinInfo());
}
