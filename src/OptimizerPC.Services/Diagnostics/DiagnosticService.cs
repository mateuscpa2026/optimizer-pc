using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Diagnostics;

/// <summary>
/// Diagnostico do computador baseado apenas em leitura: registro, contadores do Windows,
/// SMART e varredura de locais de limpeza. Nenhuma verificacao altera o sistema, e quando
/// uma fonte nao pode ser lida a verificacao aparece como nao verificada, com o motivo,
/// em vez de ser apresentada como aprovada ou reprovada.
/// </summary>
public sealed class DiagnosticService : IDiagnosticService
{
    private const string SystemCategory = "Diagnose.Category.System";
    private const string StorageCategoryKey = "Diagnose.Category.Storage";
    private const string PerformanceCategory = "Diagnose.Category.Performance";
    private const string SecurityCategory = "Diagnose.Category.Security";
    private const string MaintenanceCategory = "Diagnose.Category.Maintenance";

    private const string UnverifiedAdvice = "Diagnose.Advice.Unavailable";
    private const string HistoryCategoryKey = "History.Category.Diagnostics";

    private const string FirewallKey = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile";
    private const string DefenderPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows Defender";
    private const string DefenderRealtimeKey = @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection";
    private const string SecureBootKey = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";
    private const string UacKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string SmartScreenKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
    private const string UpdateRebootKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";
    private const string ComponentRebootKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
    private const string SessionManagerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager";

    private const long GigaByte = 1024L * 1024 * 1024;

    private readonly SystemDataCollector _collector;
    private readonly IRegistryService _registry;
    private readonly IHistoryService _history;
    private readonly ILocalizer _localizer;
    private readonly IClock _clock;
    private readonly IAppLogger _logger;
    private readonly object _sync = new();

    private DiagnosisResult? _last;

    public DiagnosticService(
        SystemDataCollector collector,
        IRegistryService registry,
        IHistoryService history,
        ILocalizer localizer,
        IClock clock,
        IAppLogger logger)
    {
        _collector = collector;
        _registry = registry;
        _history = history;
        _localizer = localizer;
        _clock = clock;
        _logger = logger;
    }

    public async Task<DiagnosisResult> RunAsync(
        DiagnosticOptions options,
        IReadOnlyList<CleanupTarget>? cleanupTargets = null,
        IProgress<DiagnosticProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var startedAt = _clock.UtcNow;
        var result = new DiagnosisResult { Id = Guid.NewGuid().ToString("N"), StartedAtUtc = startedAt };
        var writer = new CheckWriter(progress, CountPlannedChecks(options, 1));

        try
        {
            // O total de verificacoes depende da quantidade de volumes, conhecida apenas
            // depois da coleta: a barra de progresso comeca estimada e e corrigida em seguida.
            var data = await _collector.CollectAsync(options, cleanupTargets, cancellationToken).ConfigureAwait(false);
            writer.Resize(CountPlannedChecks(options, data.Volumes.Count));
            writer.Start("diag.prepare", "Diagnose.Prepare.Title");

            Evaluate(data, options, writer);
        }
        catch (OperationCanceledException)
        {
            result.WasCancelled = true;
            _logger.Info("Diagnostics", "Diagnostico cancelado pelo usuario.");
        }

        result.CompletedAtUtc = _clock.UtcNow;
        result.Checks.AddRange(writer.Checks);

        lock (_sync)
        {
            _last = result;
        }

        if (result.WasCancelled is false)
        {
            await RegisterHistoryAsync(result, cancellationToken).ConfigureAwait(false);
            _logger.Info(
                "Diagnostics",
                "Diagnostico concluido com " + result.ProblemCount + " ponto(s) de atencao em "
                + result.Duration.TotalSeconds.ToString("0.0") + "s.");
        }

        return result;
    }

    public Task<DiagnosisResult?> GetLastResultAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_last);
        }
    }

    private void Evaluate(SystemData data, DiagnosticOptions options, CheckWriter writer)
    {
        EvaluateSystem(data, writer);
        EvaluateVolumes(data, writer);

        if (options.IncludeDriveHealth)
        {
            EvaluateDrives(data, writer);
            EvaluateTemperature(data, writer);
        }

        if (options.IncludeStartupAnalysis)
        {
            EvaluateStartup(data, writer);
        }

        if (options.IncludeServiceAnalysis)
        {
            EvaluateServices(data, writer);
        }

        EvaluateProcesses(data, writer);
        EvaluateRecoverable(data, writer);
        EvaluateRecycleBin(data, writer);
        EvaluateReboot(writer);
        EvaluateUpdates(data, writer);

        if (options.IncludeDeepStorageScan)
        {
            EvaluateStorageUsage(data, writer);
        }

        if (options.IncludeSecurityChecks)
        {
            EvaluateSecurity(writer);
        }
    }

    private void EvaluateSystem(SystemData data, CheckWriter writer)
    {
        if (data.Snapshot is not { } snapshot)
        {
            writer.Add(Unverified("diag.os", SystemCategory, "Diagnose.Os.Title", "Diagnose.Os.Description"));
            writer.Add(Unverified("diag.cpu", SystemCategory, "Diagnose.Cpu.Title", "Diagnose.Cpu.Description"));
            writer.Add(Unverified("diag.memory", SystemCategory, "Diagnose.Memory.Title", "Diagnose.Memory.Description"));
            return;
        }

        var os = snapshot.Os;
        var osDetail = os.FullVersionText + " · " + os.Architecture + " · "
            + _localizer[os.IsElevated ? "Diagnose.Detail.RunningElevated" : "Diagnose.Detail.RunningStandard"];
        var uptimeDays = os.Uptime.TotalDays;

        writer.Add(new DiagnosticCheck
        {
            Id = "diag.os",
            CategoryKey = SystemCategory,
            TitleKey = "Diagnose.Os.Title",
            DescriptionKey = "Diagnose.Os.Description",
            Detail = osDetail,
            Severity = os.IsServer ? Severity.Info : Severity.Ok,
            AdviceKey = uptimeDays >= 14 ? "Diagnose.Os.AdviceUptime" : string.Empty
        });

        var cpu = snapshot.Cpu;
        var usage = data.Metrics?.CpuPercent;
        var cpuName = string.IsNullOrWhiteSpace(cpu.Name) ? _localizer["Common.Cpu.Unknown"] : cpu.Name;
        var cpuDetail = cpuName
            + (cpu.PhysicalCores + cpu.LogicalProcessors > 0
                ? " · " + Humanize.Cores(cpu.PhysicalCores, cpu.LogicalProcessors, _localizer)
                : string.Empty)
            + (usage.HasValue ? " · " + _localizer.Format("Diagnose.Detail.CpuUsage", Humanize.Percent(usage.Value, 1)) : string.Empty);

        writer.Add(new DiagnosticCheck
        {
            Id = "diag.cpu",
            CategoryKey = SystemCategory,
            TitleKey = "Diagnose.Cpu.Title",
            DescriptionKey = "Diagnose.Cpu.Description",
            Detail = cpuDetail,
            Severity = usage >= 85 ? Severity.Warning : Severity.Ok,
            AdviceKey = usage >= 85 ? "Diagnose.Cpu.AdviceHighUsage" : string.Empty
        });

        var memory = snapshot.Memory;
        var memoryDetail = Humanize.Bytes(memory.UsedBytes) + " / " + Humanize.Bytes(memory.TotalBytes)
            + " (" + Humanize.Percent(memory.UsedPercent) + ")";

        var memorySeverity = memory.UsedPercent >= 90 ? Severity.Warning : Severity.Ok;
        var lowMemory = memory.TotalBytes > 0 && memory.TotalBytes <= 4 * GigaByte;

        writer.Add(new DiagnosticCheck
        {
            Id = "diag.memory",
            CategoryKey = SystemCategory,
            TitleKey = "Diagnose.Memory.Title",
            DescriptionKey = "Diagnose.Memory.Description",
            Detail = memoryDetail,
            Severity = memorySeverity,
            AdviceKey = memorySeverity == Severity.Warning
                ? "Diagnose.Memory.AdviceHighUsage"
                : (lowMemory ? "Diagnose.Memory.AdviceLowMemory" : string.Empty)
        });
    }

    private void EvaluateVolumes(SystemData data, CheckWriter writer)
    {
        var volumes = data.Volumes.Where(volume => volume.IsReady).ToList();

        if (volumes.Count == 0)
        {
            writer.Add(Unverified("diag.volume", StorageCategoryKey, "Diagnose.Volume.Title", "Diagnose.Volume.Description"));
            return;
        }

        foreach (var volume in volumes)
        {
            var severity = volume.FreePercent switch
            {
                < 5 => Severity.Critical,
                < 12 => Severity.Warning,
                _ => Severity.Ok
            };

            writer.Add(new DiagnosticCheck
            {
                Id = "diag.volume." + volume.DriveLetter,
                CategoryKey = StorageCategoryKey,
                TitleKey = "Diagnose.Volume.Title",
                DescriptionKey = "Diagnose.Volume.Description",
                Detail = volume.DriveLetter + " · "
                    + _localizer.Format(
                        "Diagnose.Detail.VolumeFree",
                        volume.FreeText,
                        volume.TotalText,
                        Humanize.Percent(volume.FreePercent))
                    + (volume.IsSystemDrive ? " · " + _localizer["Diagnose.Detail.SystemDrive"] : string.Empty),
                Severity = severity,
                AdviceKey = severity == Severity.Ok ? string.Empty : "Diagnose.Volume.AdviceLowSpace",
                RelatedToolId = severity == Severity.Ok ? null : "cleanmgr"
            });
        }
    }

    private void EvaluateDrives(SystemData data, CheckWriter writer)
    {
        var reports = data.DriveHealth;
        if (reports.Count == 0)
        {
            writer.Add(Unverified("diag.drives", StorageCategoryKey, "Diagnose.Drives.Title", "Diagnose.Drives.Description"));
            return;
        }

        // Somente estados conhecidos entram na comparacao: um dispositivo sem dados SMART
        // nao pode esconder um disco em falha nem ser tratado como problema.
        var known = reports
            .Select(report => report.Status)
            .Where(status => status is DriveHealthStatus.Healthy or DriveHealthStatus.Warning or DriveHealthStatus.Failing)
            .ToList();

        var worst = known.Count == 0 ? DriveHealthStatus.Unknown : known.Max();
        var severity = worst switch
        {
            DriveHealthStatus.Failing => Severity.Critical,
            DriveHealthStatus.Warning => Severity.Warning,
            DriveHealthStatus.Healthy => Severity.Ok,
            _ => Severity.Info
        };

        var smartCount = reports.Count(report => report.SmartAvailable);
        var worstText = _localizer["Health.Status." + worst];
        var detail = _localizer.Format("Diagnose.Detail.Devices", reports.Count)
            + " · " + _localizer.Format("Diagnose.Detail.SmartAvailable", smartCount)
            + " · " + (known.Count == 0
                ? _localizer["Diagnose.Detail.UnknownState"]
                : _localizer.Format("Diagnose.Detail.WorstState", worstText));

        var action = severity is Severity.Warning or Severity.Critical;

        writer.Add(new DiagnosticCheck
        {
            Id = "diag.drives",
            CategoryKey = StorageCategoryKey,
            TitleKey = "Diagnose.Drives.Title",
            DescriptionKey = "Diagnose.Drives.Description",
            Detail = detail,
            Severity = severity,
            AdviceKey = action
                ? "Diagnose.Drives.AdviceCheckDisk"
                : (smartCount == 0 ? "Diagnose.Drives.AdviceNoSmart" : string.Empty),
            Elevation = action ? ElevationRequirement.Required : ElevationRequirement.None,
            RelatedToolId = action ? "chkdsk" : null
        });
    }

    private void EvaluateTemperature(SystemData data, CheckWriter writer)
    {
        var temperatures = data.DriveHealth
            .Where(report => report.TemperatureCelsius.HasValue)
            .Select(report => report.TemperatureCelsius!.Value)
            .ToList();

        if (temperatures.Count == 0)
        {
            writer.Add(Unverified("diag.temperature", StorageCategoryKey, "Diagnose.Temperature.Title", "Diagnose.Temperature.Description"));
            return;
        }

        var max = temperatures.Max();
        var severity = max switch
        {
            >= 70 => Severity.Critical,
            >= 60 => Severity.Warning,
            _ => Severity.Ok
        };

        writer.Add(new DiagnosticCheck
        {
            Id = "diag.temperature",
            CategoryKey = StorageCategoryKey,
            TitleKey = "Diagnose.Temperature.Title",
            DescriptionKey = "Diagnose.Temperature.Description",
            Detail = _localizer.Format("Diagnose.Detail.Temperature", max),
            Severity = severity,
            AdviceKey = severity == Severity.Ok ? string.Empty : "Diagnose.Temperature.Advice"
        });
    }

    private void EvaluateStartup(SystemData data, CheckWriter writer)
    {
        var entries = data.StartupEntries;
        if (entries.Count == 0)
        {
            writer.Add(new DiagnosticCheck
            {
                Id = "diag.startup",
                CategoryKey = PerformanceCategory,
                TitleKey = "Diagnose.Startup.Title",
                DescriptionKey = "Diagnose.Startup.Description",
                Severity = Severity.Ok,
                AdviceKey = string.Empty
            });

            return;
        }

        var enabled = entries.Where(entry => entry.IsEnabled).ToList();
        var high = enabled.Count(entry => entry.Impact == StartupImpact.High);
        var medium = enabled.Count(entry => entry.Impact == StartupImpact.Medium);
        var severity = enabled.Count >= 12 || high >= 3
            ? Severity.Warning
            : Severity.Ok;

        writer.Add(new DiagnosticCheck
        {
            Id = "diag.startup",
            CategoryKey = PerformanceCategory,
            TitleKey = "Diagnose.Startup.Title",
            DescriptionKey = "Diagnose.Startup.Description",
            Detail = _localizer.Format("Diagnose.Detail.StartupSummary", enabled.Count, entries.Count, high, medium),
            Severity = severity,
            AdviceKey = severity == Severity.Warning ? "Diagnose.Startup.AdviceTooMany" : string.Empty
        });
    }

    private void EvaluateServices(SystemData data, CheckWriter writer)
    {
        var services = data.Services;
        if (services.Count == 0)
        {
            writer.Add(new DiagnosticCheck
            {
                Id = "diag.services",
                CategoryKey = PerformanceCategory,
                TitleKey = "Diagnose.Services.Title",
                DescriptionKey = "Diagnose.Services.Description",
                Severity = Severity.Ok,
                AdviceKey = string.Empty
            });

            return;
        }

        var running = services.Count(service => service.State == WindowsServiceState.Running);
        var thirdParty = services.Count(service =>
            service.IsMicrosoft is false &&
            service.IsSystemCritical is false &&
            service.StartMode is ServiceStartMode.Automatic or ServiceStartMode.AutomaticDelayed);

        writer.Add(new DiagnosticCheck
        {
            Id = "diag.services",
            CategoryKey = PerformanceCategory,
            TitleKey = "Diagnose.Services.Title",
            DescriptionKey = "Diagnose.Services.Description",
            Detail = _localizer.Format("Diagnose.Detail.ServicesSummary", services.Count, running, thirdParty),
            Severity = Severity.Ok,
            AdviceKey = string.Empty
        });
    }

    private void EvaluateProcesses(SystemData data, CheckWriter writer)
    {
        var processes = data.Processes;
        var memory = data.Snapshot?.Memory;
        var detail = _localizer.Format("Diagnose.Detail.Processes", processes.Count);

        if (memory is not null)
        {
            detail += " · " + _localizer.Format("Diagnose.Detail.MemoryInUse", Humanize.Bytes(memory.UsedBytes));
        }

        writer.Add(new DiagnosticCheck
        {
            Id = "diag.processes",
            CategoryKey = PerformanceCategory,
            TitleKey = "Diagnose.Processes.Title",
            DescriptionKey = "Diagnose.Processes.Description",
            Detail = detail,
            Severity = Severity.Ok,
            AdviceKey = string.Empty
        });
    }

    private void EvaluateRecoverable(SystemData data, CheckWriter writer)
    {
        var bytes = data.RecoverableBytes;
        var severity = bytes switch
        {
            >= 10 * GigaByte => Severity.Warning,
            >= 2 * GigaByte => Severity.Info,
            _ => Severity.Ok
        };

        writer.Add(new DiagnosticCheck
        {
            Id = "diag.recoverable",
            CategoryKey = StorageCategoryKey,
            TitleKey = "Diagnose.Recoverable.Title",
            DescriptionKey = "Diagnose.Recoverable.Description",
            Detail = _localizer.Format("Diagnose.Detail.Recoverable", Humanize.Bytes(bytes)),
            Severity = severity,
            AdviceKey = severity == Severity.Ok ? string.Empty : "Diagnose.Recoverable.Advice",
            RelatedToolId = severity == Severity.Ok ? null : "cleanmgr"
        });
    }

    private void EvaluateRecycleBin(SystemData data, CheckWriter writer)
    {
        if (data.RecycleBin is not { } bin)
        {
            writer.Add(Unverified("diag.recycle-bin", StorageCategoryKey, "Diagnose.RecycleBin.Title", "Diagnose.RecycleBin.Description"));
            return;
        }

        var severity = bin.SizeBytes switch
        {
            >= 5 * GigaByte => Severity.Info,
            _ => Severity.Ok
        };

        writer.Add(new DiagnosticCheck
        {
            Id = "diag.recycle-bin",
            CategoryKey = StorageCategoryKey,
            TitleKey = "Diagnose.RecycleBin.Title",
            DescriptionKey = "Diagnose.RecycleBin.Description",
            Detail = _localizer.Format("Diagnose.Detail.RecycleBin", bin.ItemCount, bin.SizeText),
            Severity = severity,
            AdviceKey = severity == Severity.Ok ? string.Empty : "Diagnose.RecycleBin.Advice"
        });
    }

    private void EvaluateReboot(CheckWriter writer)
    {
        var id = "diag.reboot";

        try
        {
            var windowsUpdate = _registry.KeyExists(RegistryHiveKind.LocalMachine, UpdateRebootKey);
            var servicing = _registry.KeyExists(RegistryHiveKind.LocalMachine, ComponentRebootKey);
            var pendingRenames = _registry.GetMultiString(RegistryHiveKind.LocalMachine, SessionManagerKey, "PendingFileRenameOperations");
            var renames = pendingRenames?.Count(value => string.IsNullOrWhiteSpace(value) is false) ?? 0;
            var pending = windowsUpdate || servicing || renames > 0;

            writer.Add(new DiagnosticCheck
            {
                Id = id,
                CategoryKey = MaintenanceCategory,
                TitleKey = "Diagnose.Reboot.Title",
                DescriptionKey = "Diagnose.Reboot.Description",
                Detail = pending
                    ? _localizer["Diagnose.Detail.RebootPending"]
                    : _localizer["Diagnose.Detail.NoRebootPending"],
                Severity = pending ? Severity.Warning : Severity.Ok,
                AdviceKey = pending ? "Diagnose.Reboot.Advice" : string.Empty
            });
        }
        catch (Exception exception)
        {
            writer.Add(Unverified(id, MaintenanceCategory, "Diagnose.Reboot.Title", "Diagnose.Reboot.Description", exception.Message));
        }
    }

    private void EvaluateUpdates(SystemData data, CheckWriter writer)
    {
        var service = data.Services.FirstOrDefault(item =>
            string.Equals(item.Name, "wuauserv", StringComparison.OrdinalIgnoreCase));

        if (data.Services.Count == 0)
        {
            writer.Add(Unverified("diag.updates", MaintenanceCategory, "Diagnose.Updates.Title", "Diagnose.Updates.Description"));
            return;
        }

        if (service is null)
        {
            writer.Add(Unverified("diag.updates", MaintenanceCategory, "Diagnose.Updates.Title", "Diagnose.Updates.Description"));
            return;
        }

        var disabled = service.StartMode == ServiceStartMode.Disabled;
        var stopped = service.State == WindowsServiceState.Stopped;

        writer.Add(new DiagnosticCheck
        {
            Id = "diag.updates",
            CategoryKey = MaintenanceCategory,
            TitleKey = "Diagnose.Updates.Title",
            DescriptionKey = "Diagnose.Updates.Description",
            Detail = _localizer.Format(
                "Diagnose.Detail.UpdateService",
                _localizer["Services.StartMode." + service.StartMode],
                _localizer["Services.State." + service.State]),
            Severity = disabled ? Severity.Warning : Severity.Ok,
            AdviceKey = disabled ? "Diagnose.Updates.AdviceDisabled" : (stopped ? "Diagnose.Updates.AdviceStopped" : string.Empty)
        });
    }

    private void EvaluateStorageUsage(SystemData data, CheckWriter writer)
    {
        var categories = data.StorageUsage;
        if (categories.Count == 0)
        {
            writer.Add(Unverified("diag.storage", StorageCategoryKey, "Diagnose.Storage.Title", "Diagnose.Storage.Description"));
            return;
        }

        var top = categories
            .OrderByDescending(category => category.Bytes)
            .Take(3)
            .Select(category => _localizer["Storage.Category." + category.Category] + ": " + category.BytesText);

        writer.Add(new DiagnosticCheck
        {
            Id = "diag.storage",
            CategoryKey = StorageCategoryKey,
            TitleKey = "Diagnose.Storage.Title",
            DescriptionKey = "Diagnose.Storage.Description",
            Detail = _localizer.Format("Diagnose.Detail.TopFolders", string.Join(" · ", top)),
            Severity = Severity.Ok,
            AdviceKey = string.Empty
        });
    }

    private void EvaluateSecurity(CheckWriter writer)
    {
        AddRegistryFlagCheck(
            writer,
            "diag.firewall",
            FirewallKey,
            "EnableFirewall",
            "Diagnose.Firewall.Title",
            "Diagnose.Firewall.Description",
            "Diagnose.Firewall.AdviceDisabled",
            "Diagnose.Detail.FirewallOn",
            "Diagnose.Detail.FirewallOff");

        AddRegistryFlagCheck(
            writer,
            "diag.defender",
            DefenderRealtimeKey,
            "DisableRealtimeMonitoring",
            "Diagnose.Defender.Title",
            "Diagnose.Defender.Description",
            "Diagnose.Defender.AdviceDisabled",
            "Diagnose.Detail.DefenderOn",
            "Diagnose.Detail.DefenderOff");

        AddRegistryFlagCheck(
            writer,
            "diag.defender-policy",
            DefenderPolicyKey,
            "DisableAntiSpyware",
            "Diagnose.DefenderPolicy.Title",
            "Diagnose.DefenderPolicy.Description",
            "Diagnose.DefenderPolicy.AdviceDisabled",
            "Diagnose.Detail.ThirdPartyOn",
            "Diagnose.Detail.ThirdPartyOff");

        AddRegistryFlagCheck(
            writer,
            "diag.uac",
            UacKey,
            "EnableLUA",
            "Diagnose.Uac.Title",
            "Diagnose.Uac.Description",
            "Diagnose.Uac.AdviceDisabled",
            "Diagnose.Detail.UacOn",
            "Diagnose.Detail.UacOff");

        AddSecureBootCheck(writer);
        AddSmartScreenCheck(writer);
    }

    /// <summary>
    /// Le uma politica booleana documentada do Windows. Valor ausente nao e tratado como
    /// problema: significa que a politica nao foi definida nesta maquina.
    /// </summary>
    private void AddRegistryFlagCheck(
        CheckWriter writer,
        string id,
        string subKey,
        string valueName,
        string titleKey,
        string descriptionKey,
        string disabledAdviceKey,
        string enabledDetailKey,
        string disabledDetailKey)
    {
        try
        {
            var value = _registry.GetValue(RegistryHiveKind.LocalMachine, subKey, valueName);
            if (value?.IntValue is not int flag)
            {
                writer.Add(new DiagnosticCheck
                {
                    Id = id,
                    CategoryKey = SecurityCategory,
                    TitleKey = titleKey,
                    DescriptionKey = descriptionKey,
                    Detail = _localizer.Format("Diagnose.Detail.PolicyMissing", valueName),
                    Severity = Severity.Info,
                    AdviceKey = UnverifiedAdvice
                });

                return;
            }

            writer.Add(new DiagnosticCheck
            {
                Id = id,
                CategoryKey = SecurityCategory,
                TitleKey = titleKey,
                DescriptionKey = descriptionKey,
                Detail = _localizer[flag == 0 ? enabledDetailKey : disabledDetailKey],
                Severity = flag == 0 ? Severity.Ok : Severity.Warning,
                AdviceKey = flag == 0 ? string.Empty : disabledAdviceKey
            });
        }
        catch (Exception exception)
        {
            writer.Add(new DiagnosticCheck
            {
                Id = id,
                CategoryKey = SecurityCategory,
                TitleKey = titleKey,
                DescriptionKey = descriptionKey,
                Severity = Severity.Info,
                Succeeded = false,
                ErrorDetail = exception.Message,
                AdviceKey = UnverifiedAdvice
            });
        }
    }

    private void AddSecureBootCheck(CheckWriter writer)
    {
        const string id = "diag.secureboot";
        const string titleKey = "Diagnose.SecureBoot.Title";
        const string descriptionKey = "Diagnose.SecureBoot.Description";

        try
        {
            var value = _registry.GetValue(RegistryHiveKind.LocalMachine, SecureBootKey, "UEFISecureBootEnabled");
            if (value?.IntValue is not int enabled)
            {
                writer.Add(new DiagnosticCheck
                {
                    Id = id,
                    CategoryKey = SecurityCategory,
                    TitleKey = titleKey,
                    DescriptionKey = descriptionKey,
                    Detail = _localizer["Diagnose.Detail.SecureBootUnavailable"],
                    Severity = Severity.Info,
                    AdviceKey = "Diagnose.SecureBoot.AdviceUnavailable"
                });

                return;
            }

            writer.Add(new DiagnosticCheck
            {
                Id = id,
                CategoryKey = SecurityCategory,
                TitleKey = titleKey,
                DescriptionKey = descriptionKey,
                Detail = _localizer[enabled == 1 ? "Diagnose.Detail.SecureBootOn" : "Diagnose.Detail.SecureBootOff"],
                Severity = enabled == 1 ? Severity.Ok : Severity.Warning,
                AdviceKey = enabled == 1 ? string.Empty : "Diagnose.SecureBoot.AdviceDisabled"
            });
        }
        catch (Exception exception)
        {
            writer.Add(new DiagnosticCheck
            {
                Id = id,
                CategoryKey = SecurityCategory,
                TitleKey = titleKey,
                DescriptionKey = descriptionKey,
                Severity = Severity.Info,
                Succeeded = false,
                ErrorDetail = exception.Message,
                AdviceKey = UnverifiedAdvice
            });
        }
    }

    private void AddSmartScreenCheck(CheckWriter writer)
    {
        const string id = "diag.smartscreen";
        const string titleKey = "Diagnose.SmartScreen.Title";
        const string descriptionKey = "Diagnose.SmartScreen.Description";

        try
        {
            var value = _registry.GetValue(RegistryHiveKind.LocalMachine, SmartScreenKey, "SmartScreenEnabled");
            var text = value?.StringValue;

            if (string.IsNullOrWhiteSpace(text))
            {
                writer.Add(new DiagnosticCheck
                {
                    Id = id,
                    CategoryKey = SecurityCategory,
                    TitleKey = titleKey,
                    DescriptionKey = descriptionKey,
                    Detail = _localizer["Diagnose.Detail.SmartScreenUnset"],
                    Severity = Severity.Info,
                    AdviceKey = "Diagnose.SmartScreen.AdviceUnavailable"
                });

                return;
            }

            var off = string.Equals(text, "off", StringComparison.OrdinalIgnoreCase);

            writer.Add(new DiagnosticCheck
            {
                Id = id,
                CategoryKey = SecurityCategory,
                TitleKey = titleKey,
                DescriptionKey = descriptionKey,
                Detail = _localizer.Format("Diagnose.Detail.SmartScreen", text),
                Severity = off ? Severity.Warning : Severity.Ok,
                AdviceKey = off ? "Diagnose.SmartScreen.AdviceDisabled" : string.Empty
            });
        }
        catch (Exception exception)
        {
            writer.Add(new DiagnosticCheck
            {
                Id = id,
                CategoryKey = SecurityCategory,
                TitleKey = titleKey,
                DescriptionKey = descriptionKey,
                Severity = Severity.Info,
                Succeeded = false,
                ErrorDetail = exception.Message,
                AdviceKey = UnverifiedAdvice
            });
        }
    }

    private static DiagnosticCheck Unverified(string id, string categoryKey, string titleKey, string descriptionKey, string? errorDetail = null) => new()
    {
        Id = id,
        CategoryKey = categoryKey,
        TitleKey = titleKey,
        DescriptionKey = descriptionKey,
        Severity = Severity.Info,
        Succeeded = false,
        ErrorDetail = errorDetail,
        AdviceKey = UnverifiedAdvice
    };

    private static int CountPlannedChecks(DiagnosticOptions options, int volumeCount)
    {
        var total = 3;                              // sistema operacional, processador, memoria
        total += Math.Max(1, volumeCount);          // uma verificacao por volume
        total += 5;                                 // processos, recuperavel, lixeira, reiniciar, atualizacoes

        if (options.IncludeDriveHealth)
        {
            total += 2;                             // saude dos discos, temperatura
        }

        if (options.IncludeStartupAnalysis)
        {
            total += 1;
        }

        if (options.IncludeServiceAnalysis)
        {
            total += 1;
        }

        if (options.IncludeDeepStorageScan)
        {
            total += 1;
        }

        if (options.IncludeSecurityChecks)
        {
            total += 6;                             // firewall, defender, politica do defender, uac, secure boot, smartscreen
        }

        return total;
    }

    private async Task RegisterHistoryAsync(DiagnosisResult result, CancellationToken cancellationToken)
    {
        try
        {
            await _history.RecordAsync(
                new HistoryEntry
                {
                    Category = HistoryCategoryKey,
                    Action = _localizer["History.Diagnosis.Title"],
                    Description = _localizer.Format("History.Diagnosis.Description", result.Checks.Count, result.ProblemCount),
                    Result = _localizer[result.ProblemCount == 0 ? "History.Diagnosis.Clean" : "History.Diagnosis.Attention"],
                    Success = result.CriticalCount == 0,
                    Details = _localizer.Format("Diagnose.Detail.HistorySummary", result.CriticalCount, result.WarningCount)
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Warning("Diagnostics", "Nao foi possivel registrar o diagnostico no historico.", exception);
        }
    }

    /// <summary>
    /// Acumula as verificacoes e publica o progresso na mesma ordem em que sao avaliadas.
    /// </summary>
    private sealed class CheckWriter
    {
        private readonly IProgress<DiagnosticProgress>? _progress;
        private int _total;
        private int _completed;

        public CheckWriter(IProgress<DiagnosticProgress>? progress, int total)
        {
            _progress = progress;
            _total = total;
        }

        public List<DiagnosticCheck> Checks { get; } = new();

        /// <summary>Total estimado no inicio do diagnostico, corrigido apos a coleta dos volumes.</summary>
        public void Resize(int total) => _total = total;

        public void Start(string id, string titleKey) => Report(id, titleKey);

        public void Add(DiagnosticCheck check)
        {
            Checks.Add(check);
            _completed++;
            Report(check.Id, check.TitleKey);
        }

        private void Report(string id, string titleKey) =>
            _progress?.Report(new DiagnosticProgress(id, titleKey, _completed, _total));
    }
}
