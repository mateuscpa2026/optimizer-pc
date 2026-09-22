using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Interop;

namespace OptimizerPC.Services.System;

/// <summary>
/// Coleta de hardware e sistema operacional usando apenas APIs nativas do Windows
/// (kernel32, ntdll e SMBIOS). Nenhum dado sai do computador e nada e instalado.
/// </summary>
public sealed class SystemInfoService : ISystemInfoService
{
    private readonly IAppLogger _logger;

    public SystemInfoService(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<SystemSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Executa em thread de trabalho: a leitura de SMBIOS e IOCTL nao deve rodar no thread da interface.
        return Task.Run(() =>
        {
            var os = ReadOs();
            var cpu = ReadCpu();
            var memory = ReadMemory();
            var motherboard = ReadMotherboard();
            var gpus = ReadGpus();
            var devices = ReadStorageDevices();
            var volumes = VolumeEnumerator.GetVolumes();

            return new SystemSnapshot
            {
                Os = os,
                Cpu = cpu,
                Memory = memory,
                Motherboard = motherboard ?? new MotherboardInfo(),
                Gpus = gpus,
                StorageDevices = devices,
                Volumes = volumes,
                CollectedAtUtc = DateTime.UtcNow
            };
        }, cancellationToken);
    }

    public Task<OsInfo> GetOsInfoAsync(CancellationToken cancellationToken = default)
        => Task.Run(ReadOs, cancellationToken);

    public Task<CpuInfo> GetCpuInfoAsync(CancellationToken cancellationToken = default)
        => Task.Run(ReadCpu, cancellationToken);

    public Task<MemoryInfo> GetMemoryInfoAsync(CancellationToken cancellationToken = default)
        => Task.Run(ReadMemory, cancellationToken);

    public Task<IReadOnlyList<GpuInfo>> GetGpusAsync(CancellationToken cancellationToken = default)
        => Task.Run(ReadGpus, cancellationToken);

    public Task<MotherboardInfo?> GetMotherboardAsync(CancellationToken cancellationToken = default)
        => Task.Run(ReadMotherboard, cancellationToken);

    private OsInfo ReadOs()
    {
        var version = new RTL_OSVERSIONINFOW { OSVersionInfoSize = (uint)Marshal.SizeOf<RTL_OSVERSIONINFOW>() };
        var major = 0;
        var minor = 0;
        var build = 0;

        try
        {
            if (NativeKernel.RtlGetVersion(ref version) == 0)
            {
                major = (int)version.MajorVersion;
                minor = (int)version.MinorVersion;
                build = (int)version.BuildNumber;
            }
        }
        catch (Exception exception)
        {
            _logger.Warning("SystemInfo", "Nao foi possivel ler a versao do Windows via ntdll.", exception);
        }

        if (build == 0)
        {
            build = Environment.OSVersion.Version.Build;
            major = Environment.OSVersion.Version.Major;
            minor = Environment.OSVersion.Version.Minor;
        }

        var revision = ReadRevision(build);
        var displayVersion = ReadDisplayVersion();
        var productName = ReadProductName();
        var edition = ReadEdition();

        return new OsInfo
        {
            ProductName = productName,
            DisplayVersion = displayVersion,
            Edition = edition,
            Version = new Version(major, minor, build, revision),
            Build = build,
            Revision = revision,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            InstallDateUtc = ReadInstallDate(),
            IsWindows11 = major >= 10 && build >= 22000,
            IsServer = edition.Contains("Server", StringComparison.OrdinalIgnoreCase),
            ComputerName = SafeRead(() => Environment.MachineName, string.Empty),
            UserName = SafeRead(() => Environment.UserName, string.Empty),
            IsElevated = NativeProcess.IsCurrentProcessElevated(),
            Uptime = TimeSpan.FromMilliseconds(NativeKernel.GetTickCount64())
        };
    }

    private CpuInfo ReadCpu()
    {
        var topology = NativeProcessor.Query();
        var (name, manufacturer, maxClock) = ReadCpuRegistry();

        return new CpuInfo
        {
            Name = name,
            Manufacturer = manufacturer,
            PhysicalCores = topology.PhysicalCores,
            LogicalProcessors = topology.LogicalProcessors,
            MaxClockMhz = maxClock,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            L2CacheBytes = topology.L2Bytes,
            L3CacheBytes = topology.L3Bytes
        };
    }

    private static (string Name, string Manufacturer, int MaxClockMhz) ReadCpuRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key is null)
            {
                return (string.Empty, string.Empty, 0);
            }

            var name = key.GetValue("ProcessorNameString") as string ?? string.Empty;
            var manufacturer = key.GetValue("VendorIdentifier") as string ?? string.Empty;
            var clock = key.GetValue("~MHz") is int mhz ? mhz : 0;

            return (name.Trim(), manufacturer.Trim(), clock);
        }
        catch (Exception)
        {
            return (string.Empty, string.Empty, 0);
        }
    }

    private static MemoryInfo ReadMemory()
    {
        var status = MEMORYSTATUSEX.Create();
        if (!NativeKernel.GlobalMemoryStatusEx(ref status))
        {
            return new MemoryInfo();
        }

        var modules = ReadMemoryModules();

        // Quando o firmware nao informa os modulos, usa-se o total instalado reportado pelo kernel.
        var total = modules.Count > 0 ? modules.Sum(m => m.CapacityBytes) : (long)status.TotalPhysical;

        return new MemoryInfo
        {
            TotalBytes = total > 0 ? total : (long)status.TotalPhysical,
            AvailableBytes = (long)status.AvailablePhysical,
            CommittedBytes = (long)(status.TotalPageFile - status.AvailablePageFile),
            CachedBytes = 0,
            LoadPercent = (int)Math.Clamp(status.MemoryLoad, 0, 100),
            Modules = modules
        };
    }

    private static IReadOnlyList<MemoryModuleInfo> ReadMemoryModules()
    {
        var table = NativeSmbios.Read();
        if (table is null)
        {
            return Array.Empty<MemoryModuleInfo>();
        }

        var modules = new List<MemoryModuleInfo>();
        var slot = 0;

        foreach (var structure in table.Structures)
        {
            // Tipo 17: dispositivo de memoria.
            if (structure.Type != 17 || structure.Formatted.Length < 0x15)
            {
                continue;
            }

            slot++;
            var size = structure.GetWord(0x0C);

            // Bit 15 desligado significa tamanho em MB; quando ligado, o campo nao informa o tamanho.
            if ((size & 0x8000) != 0 || size == 0 || size == 0xFFFF)
            {
                continue;
            }

            modules.Add(new MemoryModuleInfo
            {
                Slot = DescribeSlot(structure, slot),
                CapacityBytes = size * 1024L * 1024L,
                SpeedMhz = structure.GetWord(0x15),
                Manufacturer = structure.GetString(structure.GetByte(0x17)),
                PartNumber = structure.GetString(structure.GetByte(0x1A)),
                FormFactor = DescribeFormFactor(structure.GetByte(0x0E)),
                MemoryType = DescribeMemoryType(structure.GetWord(0x12))
            });
        }

        return modules;
    }

    private static string DescribeSlot(NativeSmbios.SmbiosStructure structure, int index)
    {
        // 0x10: identificador do soquete informado pelo firmware (por exemplo "DIMM A1").
        var locator = structure.GetString(structure.GetByte(0x10));
        if (!string.IsNullOrWhiteSpace(locator))
        {
            return locator;
        }

        var bank = structure.GetString(structure.GetByte(0x11));
        return string.IsNullOrWhiteSpace(bank) ? "Slot " + index : bank;
    }

    private static string DescribeFormFactor(byte value) => value switch
    {
        0x09 => "DIMM",
        0x0C => "SODIMM",
        0x0D => "SRIMM",
        0x0F => "FB-DIMM",
        _ => string.Empty
    };

    private static string DescribeMemoryType(ushort value) => value switch
    {
        0x12 => "DDR",
        0x13 => "DDR2",
        0x18 => "DDR3",
        0x1A => "DDR4",
        0x22 => "DDR5",
        0x1F => "LPDDR4",
        _ => string.Empty
    };

    private static MotherboardInfo? ReadMotherboard()
    {
        var table = NativeSmbios.Read();
        if (table is null)
        {
            return null;
        }

        var info = new MotherboardInfo();
        var found = false;

        foreach (var structure in table.Structures)
        {
            switch (structure.Type)
            {
                // Tipo 2: placa-mae.
                case 2 when structure.Formatted.Length >= 0x08:
                    info = new MotherboardInfo
                    {
                        Manufacturer = structure.GetString(structure.GetByte(0x04)),
                        Product = structure.GetString(structure.GetByte(0x05)),
                        SerialNumber = structure.GetString(structure.GetByte(0x07)),
                        BiosVendor = info.BiosVendor,
                        BiosVersion = info.BiosVersion,
                        BiosReleaseDate = info.BiosReleaseDate,
                        SystemManufacturer = info.SystemManufacturer,
                        SystemModel = info.SystemModel
                    };
                    found = true;
                    break;

                // Tipo 0: BIOS.
                case 0 when structure.Formatted.Length >= 0x12:
                    info = new MotherboardInfo
                    {
                        Manufacturer = info.Manufacturer,
                        Product = info.Product,
                        SerialNumber = info.SerialNumber,
                        BiosVendor = structure.GetString(structure.GetByte(0x04)),
                        BiosVersion = structure.GetString(structure.GetByte(0x05)),
                        BiosReleaseDate = FormatBiosDate(structure.GetString(structure.GetByte(0x08))),
                        SystemManufacturer = info.SystemManufacturer,
                        SystemModel = info.SystemModel
                    };
                    found = true;
                    break;

                // Tipo 1: informacoes do sistema.
                case 1 when structure.Formatted.Length >= 0x08:
                    info = new MotherboardInfo
                    {
                        Manufacturer = info.Manufacturer,
                        Product = info.Product,
                        SerialNumber = info.SerialNumber,
                        BiosVendor = info.BiosVendor,
                        BiosVersion = info.BiosVersion,
                        BiosReleaseDate = info.BiosReleaseDate,
                        SystemManufacturer = structure.GetString(structure.GetByte(0x04)),
                        SystemModel = structure.GetString(structure.GetByte(0x05))
                    };
                    found = true;
                    break;
            }
        }

        return found ? info : null;
    }

    private static string FormatBiosDate(string raw)
    {
        // Formato SMBIOS: mm/dd/yyyy.
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return raw;
    }

    private IReadOnlyList<GpuInfo> ReadGpus()
    {
        var gpus = new List<GpuInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (key is null)
            {
                return gpus;
            }

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                // As subchaves de adaptadores sao numeradas com quatro digitos.
                if (subKeyName.Length != 4 || !subKeyName.All(char.IsDigit))
                {
                    continue;
                }

                using var adapter = key.OpenSubKey(subKeyName);
                if (adapter is null)
                {
                    continue;
                }

                var name = adapter.GetValue("DriverDesc") as string ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                {
                    continue;
                }

                var vendor = adapter.GetValue("ProviderName") as string ?? string.Empty;
                var driverVersion = adapter.GetValue("DriverVersion") as string ?? string.Empty;
                var memory = ReadAdapterMemory(adapter);

                gpus.Add(new GpuInfo
                {
                    Name = name.Trim(),
                    Vendor = vendor.Trim(),
                    DriverVersion = driverVersion,
                    AdapterMemoryBytes = memory,
                    Resolution = string.Empty
                });
            }
        }
        catch (Exception exception)
        {
            _logger.Warning("SystemInfo", "Nao foi possivel enumerar os adaptadores de video.", exception);
        }

        return gpus;
    }

    private static long ReadAdapterMemory(RegistryKey adapter)
    {
        // HardwareInformation.qwMemorySize e um QWORD; MemorySize e um DWORD de 32 bits.
        try
        {
            if (adapter.GetValue("HardwareInformation.qwMemorySize") is long { } qword && qword > 0)
            {
                return qword;
            }

            if (adapter.GetValue("HardwareInformation.MemorySize") is byte[] bytes && bytes.Length >= 4)
            {
                return BitConverter.ToUInt32(bytes, 0);
            }
        }
        catch (Exception)
        {
            // Adaptadores que nao expoe memoria dedicada seguem sem esse dado.
        }

        return 0;
    }

    private IReadOnlyList<StorageDeviceInfo> ReadStorageDevices()
    {
        var disks = new List<StorageDeviceInfo>();

        try
        {
            foreach (var disk in StorageDeviceReader.ReadAll())
            {
                var mediaType = InferMediaType(disk);
                var busType = (StorageBusType)disk.BusType;

                disks.Add(new StorageDeviceInfo
                {
                    DeviceIndex = disk.Index,
                    Model = disk.Model,
                    SerialNumber = disk.SerialNumber,
                    FirmwareRevision = disk.FirmwareRevision,
                    SizeBytes = disk.SizeBytes,
                    BusType = Enum.IsDefined(busType) ? busType : StorageBusType.Unknown,
                    MediaType = mediaType,
                    RotationRateRpm = null,
                    Health = DriveHealthStatus.Unknown,
                    TemperatureCelsius = null,
                    SmartSupported = false,
                    PowerOnHours = null,
                    HealthDetail = string.Empty
                });
            }
        }
        catch (Exception exception)
        {
            _logger.Warning("SystemInfo", "Nao foi possivel consultar os discos fisicos.", exception);
        }

        return disks;
    }

    /// <summary>
    /// Deduz o tipo de midia pelo barramento e pela penalidade de busca.
    /// Quando nao ha informacao suficiente, o resultado fica como desconhecido.
    /// </summary>
    internal static StorageMediaType InferMediaType(StorageDeviceReader.PhysicalDisk disk)
    {
        if (disk.Removable)
        {
            return StorageMediaType.Unknown;
        }

        if (disk.BusType == (byte)StorageBusType.Nvme)
        {
            return StorageMediaType.Ssd;
        }

        return disk.SeekPenalty ? StorageMediaType.Hdd : StorageMediaType.Ssd;
    }

    private static int ReadRevision(int build)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key?.GetValue("UBR") is int ubr)
            {
                return ubr;
            }
        }
        catch (Exception)
        {
            // Sem o UBR, a revisao permanece zero.
        }

        return 0;
    }

    private static string ReadDisplayVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return (key?.GetValue("DisplayVersion") as string ?? string.Empty).Trim();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string ReadProductName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var name = (key?.GetValue("ProductName") as string ?? string.Empty).Trim();

            // O Windows 11 mantem "Windows 10" nesse campo; o nome correto vem de DisplayVersion/edicao.
            if (Environment.OSVersion.Version.Build >= 22000 && name.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
            {
                return name.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
            }

            return name;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string ReadEdition()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return (key?.GetValue("EditionID") as string ?? string.Empty).Trim();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static DateTime? ReadInstallDate()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key?.GetValue("InstallDate") is int seconds && seconds > 0)
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
            }
        }
        catch (Exception)
        {
            // A data de instalacao nem sempre esta disponivel.
        }

        return null;
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
}
