using OptimizerPC.Core;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.System;

/// <summary>
/// Leitura das unidades montadas (letra, rotulo, sistema de arquivos e espaco livre).
/// Somente leitura: nenhuma unidade e formatada, montada ou alterada.
/// </summary>
internal static class VolumeEnumerator
{
    internal static IReadOnlyList<VolumeInfo> GetVolumes()
    {
        var volumes = new List<VolumeInfo>();
        var systemRoot = GetSystemDriveRoot();

        foreach (var drive in SafeGetDrives())
        {
            try
            {
                var root = drive.Name;
                var isReady = drive.IsReady;

                var label = string.Empty;
                var fileSystem = string.Empty;

                if (isReady)
                {
                    label = SafeRead(() => drive.VolumeLabel, string.Empty);
                    fileSystem = SafeRead(() => drive.DriveFormat, string.Empty);
                }

                volumes.Add(new VolumeInfo
                {
                    DriveLetter = root.TrimEnd('\\', ':'),
                    RootPath = root,
                    Label = label,
                    FileSystem = fileSystem,
                    TotalBytes = isReady ? SafeRead(() => drive.TotalSize, 0L) : 0,
                    FreeBytes = isReady ? SafeRead(() => drive.AvailableFreeSpace, 0L) : 0,
                    DriveKind = DescribeDriveKind(drive.DriveType),
                    IsSystemDrive = string.Equals(root, systemRoot, StringComparison.OrdinalIgnoreCase),
                    IsReady = isReady
                });
            }
            catch (Exception)
            {
                // Unidades que desaparecem durante a leitura sao ignoradas.
            }
        }

        return volumes
            .OrderByDescending(v => v.IsSystemDrive)
            .ThenBy(v => v.DriveLetter, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string GetSystemDriveRoot()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            return string.IsNullOrEmpty(root) ? "C:\\" : root;
        }
        catch (Exception)
        {
            return "C:\\";
        }
    }

    internal static StorageCategory CategorizeVolume(VolumeInfo volume) =>
        volume.IsSystemDrive ? StorageCategory.System : StorageCategory.Other;

    private static IEnumerable<DriveInfo> SafeGetDrives()
    {
        try
        {
            return DriveInfo.GetDrives();
        }
        catch (Exception)
        {
            return Array.Empty<DriveInfo>();
        }
    }

    private static VolumeDriveKind DescribeDriveKind(DriveType type) => type switch
    {
        DriveType.Fixed => VolumeDriveKind.Fixed,
        DriveType.Removable => VolumeDriveKind.Removable,
        DriveType.Network => VolumeDriveKind.Network,
        DriveType.CDRom => VolumeDriveKind.CdRom,
        DriveType.Ram => VolumeDriveKind.Ram,
        DriveType.NoRootDirectory => VolumeDriveKind.NoRootDirectory,
        _ => VolumeDriveKind.Unknown
    };

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
