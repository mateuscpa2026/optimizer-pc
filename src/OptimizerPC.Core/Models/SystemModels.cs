namespace OptimizerPC.Core.Models;

public sealed class OsInfo
{
    public string ProductName { get; init; } = string.Empty;
    public string DisplayVersion { get; init; } = string.Empty;
    public string Edition { get; init; } = string.Empty;
    public Version Version { get; init; } = new(0, 0);
    public int Build { get; init; }
    public int Revision { get; init; }
    public string Architecture { get; init; } = string.Empty;
    public DateTime? InstallDateUtc { get; init; }
    public bool IsWindows11 { get; init; }
    public bool IsServer { get; init; }
    public string ComputerName { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public bool IsElevated { get; init; }
    public TimeSpan Uptime { get; init; }

    public string FullVersionText =>
        $"{(string.IsNullOrWhiteSpace(ProductName) ? "Windows" : ProductName)}" +
        $"{(string.IsNullOrWhiteSpace(DisplayVersion) ? string.Empty : " " + DisplayVersion)}" +
        $" (build {Build}.{Revision})";
}

public sealed class CpuInfo
{
    public string Name { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public int PhysicalCores { get; init; }
    public int LogicalProcessors { get; init; }
    public int MaxClockMhz { get; init; }
    public string Architecture { get; init; } = string.Empty;
    public long L2CacheBytes { get; init; }
    public long L3CacheBytes { get; init; }
}

public sealed class MemoryModuleInfo
{
    public string Slot { get; init; } = string.Empty;
    public long CapacityBytes { get; init; }
    public int SpeedMhz { get; init; }
    public string Manufacturer { get; init; } = string.Empty;
    public string PartNumber { get; init; } = string.Empty;
    public string FormFactor { get; init; } = string.Empty;
    public string MemoryType { get; init; } = string.Empty;
}

public sealed class MemoryInfo
{
    public long TotalBytes { get; init; }
    public long AvailableBytes { get; init; }
    public long CommittedBytes { get; init; }
    public long CachedBytes { get; init; }
    public int LoadPercent { get; init; }
    public IReadOnlyList<MemoryModuleInfo> Modules { get; init; } = Array.Empty<MemoryModuleInfo>();

    public long UsedBytes => Math.Max(0, TotalBytes - AvailableBytes);

    public double UsedPercent => TotalBytes <= 0 ? 0 : UsedBytes * 100.0 / TotalBytes;
}

public sealed class GpuInfo
{
    public string Name { get; init; } = string.Empty;
    public string Vendor { get; init; } = string.Empty;
    public string DriverVersion { get; init; } = string.Empty;
    public long AdapterMemoryBytes { get; init; }
    public string Resolution { get; init; } = string.Empty;
}

public sealed class MotherboardInfo
{
    public string Manufacturer { get; init; } = string.Empty;
    public string Product { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public string BiosVendor { get; init; } = string.Empty;
    public string BiosVersion { get; init; } = string.Empty;
    public string BiosReleaseDate { get; init; } = string.Empty;
    public string SystemManufacturer { get; init; } = string.Empty;
    public string SystemModel { get; init; } = string.Empty;
}

/// <summary>Retrato completo do computador coletado uma vez e reutilizado pela interface.</summary>
public sealed class SystemSnapshot
{
    public OsInfo Os { get; init; } = new();
    public CpuInfo Cpu { get; init; } = new();
    public MemoryInfo Memory { get; init; } = new();
    public MotherboardInfo Motherboard { get; init; } = new();
    public IReadOnlyList<GpuInfo> Gpus { get; init; } = Array.Empty<GpuInfo>();
    public IReadOnlyList<StorageDeviceInfo> StorageDevices { get; init; } = Array.Empty<StorageDeviceInfo>();
    public IReadOnlyList<VolumeInfo> Volumes { get; init; } = Array.Empty<VolumeInfo>();
    public DateTime CollectedAtUtc { get; init; } = DateTime.UtcNow;

    public GpuInfo? PrimaryGpu => Gpus.Count > 0 ? Gpus[0] : null;

    public VolumeInfo? SystemVolume =>
        Volumes.FirstOrDefault(v => v.IsSystemDrive) ?? Volumes.FirstOrDefault();
}
