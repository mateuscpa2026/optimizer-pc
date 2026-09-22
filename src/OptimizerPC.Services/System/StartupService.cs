using System.Diagnostics;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Interop;
using OptimizerPC.Services.Restore;

namespace OptimizerPC.Services.System;

/// <summary>
/// Itens de inicializacao do Windows (chaves Run, pastas Inicializar e o historico
/// StartupApproved). Habilitar e desabilitar usa exatamente o mesmo mecanismo do
/// Gerenciador de Tarefas: o valor original nunca e apagado, apenas marcado,
/// e toda alteracao registra um ponto de reversao.
/// </summary>
public sealed class StartupService : IStartupService
{
    private const string RunSubKey = @"Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedSubKey = @"Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedSubKey32 = @"Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedFolderSubKey = @"Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    private const string WowNode = @"WOW6432Node\";

    private static readonly byte[] EnabledPayload = { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    private static readonly byte[] DisabledPayload = { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    private readonly IRegistryService _registry;
    private readonly IFileSystemService _fileSystem;
    private readonly IRestoreService _restore;
    private readonly ILocalizer _localizer;
    private readonly IAppLogger _logger;

    public StartupService(
        IRegistryService registry,
        IFileSystemService fileSystem,
        IRestoreService restore,
        ILocalizer localizer,
        IAppLogger logger)
    {
        _registry = registry;
        _fileSystem = fileSystem;
        _restore = restore;
        _localizer = localizer;
        _logger = logger;
    }

    public Task<IReadOnlyList<StartupEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<StartupEntry>>(() => ReadEntries(cancellationToken), cancellationToken);

    public async Task<ActionExecutionResult> SetEnabledAsync(StartupEntry entry, bool enabled, CancellationToken cancellationToken = default)
    {
        var approvedHive = entry.Location is StartupLocation.LocalMachineRun or StartupLocation.LocalMachineRun32
            ? RegistryHiveKind.LocalMachine
            : RegistryHiveKind.CurrentUser;

        if (approvedHive == RegistryHiveKind.LocalMachine && !NativeProcess.IsCurrentProcessElevated())
        {
            return Blocked(entry, "Startup.Error.NeedsElevation");
        }

        var approvedSubKey = ApprovedSubKeyFor(entry.Location);
        if (approvedSubKey is null)
        {
            return Blocked(entry, "Startup.Error.NotSupported");
        }

        var valueName = ApprovedValueName(entry);
        byte[]? previous = null;

        var current = _registry.GetValue(approvedHive, approvedSubKey, valueName);
        if (current?.StringValue is { Length: > 0 } base64)
        {
            try
            {
                previous = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                previous = null;
            }
        }

        try
        {
            _registry.SetBinary(approvedHive, approvedSubKey, valueName, enabled ? EnabledPayload : DisabledPayload);
        }
        catch (Exception exception)
        {
            _logger.Warning("Startup", "Nao foi possivel alterar o estado de " + entry.Name + ".", exception);
            return Blocked(entry, "Startup.Error.WriteFailed");
        }

        var payload = RestorePayload.ForToggle(approvedHive.ToString(), approvedSubKey, valueName, previous, enabled);
        var recordId = await _restore.RegisterAsync(
            new RestoreRecord
            {
                Kind = RestoreRecordKind.StartupEntryState,
                TitleKey = "Restore.Kind.StartupEntryState",
                Description = entry.Name,
                SourceAction = enabled ? "EnableStartupEntry" : "DisableStartupEntry",
                TargetPath = entry.ExecutablePath ?? entry.Command,
                PayloadJson = payload
            },
            cancellationToken).ConfigureAwait(false);

        _logger.Info("Startup", "Item de inicializacao " + (enabled ? "habilitado: " : "desabilitado: ") + entry.Name);

        return new ActionExecutionResult
        {
            ActionId = entry.Id,
            Kind = enabled ? OptimizationActionKind.EnableStartupEntry : OptimizationActionKind.DisableStartupEntry,
            TitleKey = enabled ? "Startup.Action.Enable" : "Startup.Action.Disable",
            Success = true,
            MessageKey = enabled ? "Startup.Result.Enabled" : "Startup.Result.Disabled",
            Detail = entry.Name,
            RestoreRecordId = recordId
        };
    }

    private IReadOnlyList<StartupEntry> ReadEntries(CancellationToken cancellationToken)
    {
        var entries = new List<StartupEntry>();

        ReadRunKey(entries, RegistryHiveKind.CurrentUser, @"Software\" + RunSubKey, StartupLocation.CurrentUserRun, ApprovedSubKey);
        ReadRunKey(entries, RegistryHiveKind.CurrentUser, @"Software\" + WowNode + RunSubKey, StartupLocation.CurrentUserRun32, ApprovedSubKey32);
        ReadRunKey(entries, RegistryHiveKind.LocalMachine, @"SOFTWARE\" + RunSubKey, StartupLocation.LocalMachineRun, ApprovedSubKey);
        ReadRunKey(entries, RegistryHiveKind.LocalMachine, @"SOFTWARE\" + WowNode + RunSubKey, StartupLocation.LocalMachineRun32, ApprovedSubKey32);

        ReadStartupFolder(entries, Environment.SpecialFolder.Startup, StartupLocation.CurrentUserStartupFolder, RegistryHiveKind.CurrentUser);
        ReadStartupFolder(entries, Environment.SpecialFolder.CommonStartup, StartupLocation.AllUsersStartupFolder, RegistryHiveKind.LocalMachine);

        cancellationToken.ThrowIfCancellationRequested();

        return entries
            .OrderBy(e => _localizer["Startup.Location." + e.Location], StringComparer.CurrentCulture)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private void ReadRunKey(List<StartupEntry> entries, RegistryHiveKind hive, string subKey, StartupLocation location, string approvedSubKey)
    {
        var approvedHive = location is StartupLocation.LocalMachineRun or StartupLocation.LocalMachineRun32
            ? RegistryHiveKind.LocalMachine
            : RegistryHiveKind.CurrentUser;

        foreach (var name in _registry.GetValueNames(hive, subKey))
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var value = _registry.GetValue(hive, subKey, name);
            var command = value?.StringValue ?? string.Empty;
            var executable = ResolveExecutablePath(command);

            entries.Add(new StartupEntry
            {
                Id = location + "|" + name,
                Name = name,
                Command = command,
                ExecutablePath = executable,
                Publisher = ReadPublisher(executable),
                Location = location,
                IsEnabled = ReadEnabledState(approvedHive, approvedSubKey, name),
                SupportsToggle = true,
                RequiresElevation = location is StartupLocation.LocalMachineRun or StartupLocation.LocalMachineRun32,
                Impact = StartupImpact.Unknown,
                ExecutableSizeBytes = ReadExecutableSize(executable)
            });
        }
    }

    private void ReadStartupFolder(List<StartupEntry> entries, Environment.SpecialFolder folder, StartupLocation location, RegistryHiveKind approvedHive)
    {
        var root = SafeFolderPath(folder);
        if (string.IsNullOrEmpty(root) || !_fileSystem.DirectoryExists(root))
        {
            return;
        }

        foreach (var path in SafeEnumerate(root))
        {
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.Add(new StartupEntry
            {
                Id = location + "|" + fileName,
                Name = Path.GetFileNameWithoutExtension(fileName),
                Command = path,
                ExecutablePath = path,
                Publisher = ReadPublisher(path),
                Location = location,
                IsEnabled = ReadEnabledState(approvedHive, ApprovedFolderSubKey, fileName),
                SupportsToggle = true,
                RequiresElevation = location == StartupLocation.AllUsersStartupFolder,
                Impact = StartupImpact.Unknown,
                ExecutableSizeBytes = ReadExecutableSize(path)
            });
        }
    }

    private IEnumerable<string> SafeEnumerate(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception exception)
        {
            _logger.Warning("Startup", "Nao foi possivel ler a pasta de inicializacao " + root + ".", exception);
            return Array.Empty<string>();
        }
    }

    private static string SafeFolderPath(Environment.SpecialFolder folder)
    {
        try
        {
            return Environment.GetFolderPath(folder);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// O Windows guarda o estado de habilitacao em StartupApproved. A ausencia do valor
    /// significa que o item nunca foi alterado e, portanto, esta habilitado.
    /// </summary>
    private bool ReadEnabledState(RegistryHiveKind hive, string approvedSubKey, string valueName)
    {
        var value = _registry.GetValue(hive, approvedSubKey, valueName);
        if (value?.StringValue is not { Length: > 0 } base64)
        {
            return true;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length == 0)
            {
                return true;
            }

            // O primeiro byte alterna entre habilitado (par) e desabilitado (impar).
            return bytes[0] % 2 == 0;
        }
        catch (FormatException)
        {
            return true;
        }
    }

    private static string? ApprovedSubKeyFor(StartupLocation location) => location switch
    {
        StartupLocation.CurrentUserRun => ApprovedSubKey,
        StartupLocation.LocalMachineRun => ApprovedSubKey,
        StartupLocation.CurrentUserRun32 => ApprovedSubKey32,
        StartupLocation.LocalMachineRun32 => ApprovedSubKey32,
        StartupLocation.CurrentUserStartupFolder => ApprovedFolderSubKey,
        StartupLocation.AllUsersStartupFolder => ApprovedFolderSubKey,
        _ => null
    };

    private static string ApprovedValueName(StartupEntry entry)
    {
        var separator = entry.Id.IndexOf('|');
        return separator >= 0 ? entry.Id[(separator + 1)..] : entry.Name;
    }

    private static ActionExecutionResult Blocked(StartupEntry entry, string messageKey) => new()
    {
        ActionId = entry.Id,
        Kind = entry.IsEnabled ? OptimizationActionKind.DisableStartupEntry : OptimizationActionKind.EnableStartupEntry,
        TitleKey = "Startup.Action.Toggle",
        Success = false,
        Skipped = true,
        MessageKey = messageKey,
        Detail = entry.Name
    };

    /// <summary>
    /// Extrai o caminho do executavel a partir da linha de comando registrada,
    /// expandindo variaveis de ambiente quando presentes.
    /// </summary>
    internal static string? ResolveExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = command.Trim();

        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            text = end > 1 ? text[1..end] : text.Trim('"');
        }
        else
        {
            var space = text.IndexOf(' ');
            if (space > 0)
            {
                text = text[..space];
            }
        }

        try
        {
            text = Environment.ExpandEnvironmentVariables(text);
        }
        catch (Exception)
        {
            // Comandos com variaveis invalidas seguem sem expansao.
        }

        if (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || text.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        return text.Contains(Path.DirectorySeparatorChar) || text.Contains(Path.AltDirectorySeparatorChar) ? text : null;
    }

    private static string ReadPublisher(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
        {
            return string.Empty;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return (info.CompanyName ?? string.Empty).Trim();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static long? ReadExecutableSize(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(executablePath);
            return info.Exists ? info.Length : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
