using OptimizerPC.Core.Formatting;

namespace OptimizerPC.Core.Models;

public sealed class ProcessInfoModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Company { get; init; } = string.Empty;
    public string? FilePath { get; init; }
    public long WorkingSetBytes { get; set; }
    public long PrivateBytes { get; set; }
    public double CpuPercent { get; set; }
    public double DiskBytesPerSecond { get; set; }
    public double NetworkBytesPerSecond { get; set; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
    public DateTime? StartTimeUtc { get; init; }
    public bool IsSystemCritical { get; init; }
    public bool IsElevatedProcess { get; init; }

    public string MemoryText => Humanize.Bytes(WorkingSetBytes);

    public string CpuText => Humanize.Percent(CpuPercent, 1);

    public bool HasCpuActivity(double threshold = 0.1) => CpuPercent >= threshold;

    public string IdentityText => Id + " · " + Name;
}

public sealed class StartupEntry
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public string Publisher { get; set; } = string.Empty;
    public StartupLocation Location { get; init; }
    public bool IsEnabled { get; set; }
    public bool SupportsToggle { get; init; }
    public bool RequiresElevation { get; init; }
    public StartupImpact Impact { get; set; } = StartupImpact.Unknown;
    public string ImpactReasonKey { get; set; } = string.Empty;
    public long? ExecutableSizeBytes { get; set; }

    public StartupImpact RecalculateImpact()
    {
        if (ExecutableSizeBytes is > 40L * 1024 * 1024)
        {
            ImpactReasonKey = "Startup.Impact.LargeExecutable";
            return StartupImpact.High;
        }

        if (ExecutableSizeBytes is > 8L * 1024 * 1024)
        {
            ImpactReasonKey = "Startup.Impact.MediumExecutable";
            return StartupImpact.Medium;
        }

        if (Location is StartupLocation.LocalMachineRun or StartupLocation.LocalMachineRun32)
        {
            ImpactReasonKey = "Startup.Impact.MachineScope";
            return StartupImpact.Medium;
        }

        ImpactReasonKey = "Startup.Impact.EstimatedLow";
        return StartupImpact.Low;
    }
}

public sealed class WindowsServiceInfo
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public WindowsServiceState State { get; set; } = WindowsServiceState.Unknown;
    public ServiceStartMode StartMode { get; set; } = ServiceStartMode.Unknown;
    public int ProcessId { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public bool IsSystemCritical { get; init; }
    public bool CanStop { get; set; }
    public bool IsMicrosoft { get; set; }
}

public sealed class PowerPlanInfo
{
    public Guid SchemeGuid { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsWellKnown { get; init; }
    public PowerPlanKind Kind { get; init; } = PowerPlanKind.Custom;
}

public enum PowerPlanKind
{
    Custom = 0,
    Balanced = 1,
    HighPerformance = 2,
    PowerSaver = 3
}

/// <summary>Ferramenta nativa do Windows exposta na tela "Ferramentas".</summary>
public sealed class WindowsToolDescriptor
{
    public string Id { get; init; } = string.Empty;
    public string TitleKey { get; init; } = string.Empty;
    public string DescriptionKey { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public IReadOnlyList<string> DefaultArguments { get; init; } = Array.Empty<string>();
    public ElevationRequirement Elevation { get; init; } = ElevationRequirement.None;
    public bool IsAdvanced { get; init; }
    public string? NoteKey { get; init; }
}

public sealed class MetricSample
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public double CpuPercent { get; init; }
    public double MemoryPercent { get; init; }
    public long MemoryUsedBytes { get; init; }
    public long MemoryTotalBytes { get; init; }
    public double DiskActivityPercent { get; init; }
    public double DiskReadBytesPerSecond { get; init; }
    public double DiskWriteBytesPerSecond { get; init; }
    public double NetworkDownloadBytesPerSecond { get; init; }
    public double NetworkUploadBytesPerSecond { get; init; }
    public int ProcessCount { get; init; }
    public int ThreadCount { get; init; }

    public bool DiskActivityAvailable { get; init; } = true;

    public bool NetworkActivityAvailable { get; init; } = true;
}
