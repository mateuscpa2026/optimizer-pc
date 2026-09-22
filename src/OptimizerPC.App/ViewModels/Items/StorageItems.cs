using System.IO;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.ViewModels.Items;

/// <summary>Volume montado no sistema, com espaco usado e livre.</summary>
public sealed class VolumeItemViewModel : ItemViewModelBase
{
    public VolumeItemViewModel(VolumeInfo model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public VolumeInfo Model { get; }

    public string DriveLetter => Model.DriveLetter;

    public string Label => Model.Label;

    public string Title => string.IsNullOrWhiteSpace(Model.Label)
        ? Localizer.Format("Storage.Volume.Unlabeled", Model.DriveLetter)
        : Localizer.Format("Storage.Volume.Labeled", Model.Label, Model.DriveLetter);

    public string RootPath => Model.RootPath;

    public string FileSystem => Model.FileSystem;

    public bool IsSystemDrive => Model.IsSystemDrive;

    public bool IsReady => Model.IsReady;

    public string DriveTypeText => Humanize.DriveKind(Model.DriveKind, Localizer);

    public long TotalBytes => Model.TotalBytes;

    public string TotalText => Model.TotalText;

    public string FreeText => Model.FreeText;

    public string UsedText => Model.UsedText;

    public double UsedPercent => Model.UsedPercent;

    public string UsedPercentText => Humanize.Percent(Model.UsedPercent);

    public string FreePercentText => Humanize.Percent(Model.FreePercent);

    /// <summary>Menos de 10% livre: espaco insuficiente para atualizacoes e arquivos temporarios.</summary>
    public bool IsLowOnSpace => Model.IsReady && Model.FreePercent < 10;

    public string LowSpaceText => Localizer.Format("Storage.Volume.LowSpace", Model.FreeText);
}

/// <summary>Dispositivo fisico de armazenamento com dados de SMART quando disponiveis.</summary>
public sealed class StorageDeviceItemViewModel : ItemViewModelBase
{
    public StorageDeviceItemViewModel(StorageDeviceInfo model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public StorageDeviceInfo Model { get; }

    public int DeviceIndex => Model.DeviceIndex;

    public string Title => string.IsNullOrWhiteSpace(Model.Model)
        ? Localizer["Storage.Device.UnknownModel"]
        : Model.Model;

    public string IndexText => Localizer.Format("Storage.Device.Index", Model.DeviceIndex);

    public long SizeBytes => Model.SizeBytes;

    public string SizeText => Model.SizeText;

    public StorageMediaType MediaType => Model.MediaType;

    public string MediaText => Humanize.MediaType(Model.MediaType, Localizer);

    public StorageBusType BusType => Model.BusType;

    public string BusText => Humanize.BusType(Model.BusType, Localizer);

    public DriveHealthStatus Health => Model.Health;

    public string HealthText => Localizer["Health.Status." + Model.Health];

    public string HealthDetail => Model.HealthDetail;

    public bool HasHealthDetail => string.IsNullOrWhiteSpace(Model.HealthDetail) is false;

    public bool SmartAvailable => Model.SmartSupported;

    public string SmartText => Localizer[Model.SmartSupported
        ? "Storage.Device.Smart.Available"
        : "Storage.Device.Smart.Unavailable"];

    public bool HasTemperature => Model.TemperatureCelsius.HasValue;

    public string TemperatureText => Model.TemperatureCelsius.HasValue
        ? Model.TemperatureCelsius.Value + " °C"
        : Localizer["Common.NotAvailable"];

    public bool HasPowerOnHours => Model.PowerOnHours.HasValue;

    public string PowerOnHoursText => Model.PowerOnHours.HasValue
        ? Localizer.Format("Storage.Device.PowerOnHours", Model.PowerOnHours.Value)
        : Localizer["Common.NotAvailable"];

    public string SerialNumber => Model.SerialNumber;

    public bool HasSerialNumber => string.IsNullOrWhiteSpace(Model.SerialNumber) is false;

    public string FirmwareRevision => Model.FirmwareRevision;

    public bool HasFirmware => string.IsNullOrWhiteSpace(Model.FirmwareRevision) is false;
}

/// <summary>Categoria de uso de espaco (aplicativos, documentos, temporarios...).</summary>
public sealed class StorageCategoryItemViewModel : ItemViewModelBase
{
    public StorageCategoryItemViewModel(StorageCategoryUsage model, long totalBytes, ILocalizer localizer)
        : base(localizer)
    {
        Model = model;
        TotalBytes = totalBytes;
    }

    public StorageCategoryUsage Model { get; }

    public long TotalBytes { get; }

    public StorageCategory Category => Model.Category;

    public string CategoryText => Localizer["Storage.Category." + Model.Category];

    public long Bytes => Model.Bytes;

    public string BytesText => Model.BytesText;

    public long FileCount => Model.FileCount;

    public string FilesText => Localizer.Format("Storage.Category.Files", Model.FileCount);

    public double SharePercent => TotalBytes <= 0 ? 0 : Math.Clamp(Model.Bytes * 100.0 / TotalBytes, 0, 100);

    public string ShareText => Humanize.Percent(SharePercent);
}

/// <summary>Arquivo grande localizado na varredura de armazenamento.</summary>
public sealed class LargeFileItemViewModel : ItemViewModelBase
{
    public LargeFileItemViewModel(LargeFileInfo model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public LargeFileInfo Model { get; }

    public string FullPath => Model.FullPath;

    public string FileName => Model.FileName;

    public string Folder => Model.Folder;

    public string Extension => string.IsNullOrWhiteSpace(Model.Extension)
        ? Localizer["Storage.File.NoExtension"]
        : Model.Extension;

    public long SizeBytes => Model.SizeBytes;

    public string SizeText => Model.SizeText;

    public string LastWriteText => Model.LastWriteText;
}

/// <summary>Arquivo individual dentro de um grupo de duplicados.</summary>
public sealed class DuplicateFileItemViewModel : ItemViewModelBase
{
    public DuplicateFileItemViewModel(DuplicateFileInfo model, bool isOldest, ILocalizer localizer)
        : base(localizer)
    {
        Model = model;
        IsOldest = isOldest;
    }

    public DuplicateFileInfo Model { get; }

    public bool IsOldest { get; }

    public string FullPath => Model.FullPath;

    public string FileName => Path.GetFileName(Model.FullPath);

    public string SizeText => Model.SizeText;

    public string LastWriteText => Model.LastWriteText;

    public string OldestText => Localizer["Storage.Duplicates.Oldest"];
}

/// <summary>Grupo de arquivos identicos por conteudo.</summary>
public sealed class DuplicateGroupItemViewModel : ItemViewModelBase
{
    public DuplicateGroupItemViewModel(DuplicateGroup model, ILocalizer localizer)
        : base(localizer)
    {
        Model = model;
        var oldestPath = model.Files.Count == 0
            ? null
            : model.Files.OrderBy(f => f.LastWriteTimeUtc).First().FullPath;

        Files = model.Files
            .Select(f => new DuplicateFileItemViewModel(f, string.Equals(f.FullPath, oldestPath, StringComparison.OrdinalIgnoreCase), localizer))
            .ToList();
    }

    public DuplicateGroup Model { get; }

    public IReadOnlyList<DuplicateFileItemViewModel> Files { get; }

    public string Extension => string.IsNullOrWhiteSpace(Model.Extension)
        ? Localizer["Storage.File.NoExtension"]
        : Model.Extension;

    public string Title => Localizer.Format("Storage.Duplicates.Title", FileCount, Extension);

    public int FileCount => Model.Files.Count;

    public string FilesText => Localizer.Format("Storage.Duplicates.Files", FileCount);

    public long FileSizeBytes => Model.FileSizeBytes;

    public string FileSizeText => Model.FileSizeText;

    public long WastedBytes => Model.WastedBytes;

    public string WastedText => Model.WastedText;

    public string Hash => Model.Hash;
}

/// <summary>Jogo instalado detectado localmente.</summary>
public sealed class GameItemViewModel : ItemViewModelBase
{
    public GameItemViewModel(InstalledGame model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public InstalledGame Model { get; }

    public string Name => Model.Name;

    public string InstallPath => Model.InstallPath;

    public bool HasInstallPath => string.IsNullOrWhiteSpace(Model.InstallPath) is false;

    public string? ExecutablePath => Model.ExecutablePath;

    public bool HasExecutable => string.IsNullOrWhiteSpace(Model.ExecutablePath) is false;

    public GameLauncherKind Launcher => Model.Launcher;

    public string LauncherText => Localizer["Gamer.Launcher." + Model.Launcher];

    public long SizeBytes => Model.SizeBytes;

    public string SizeText => Model.SizeBytes > 0 ? Humanize.Bytes(Model.SizeBytes) : Localizer["Common.NotAvailable"];

    public bool HasSize => Model.SizeBytes > 0;
}

/// <summary>Ferramenta nativa do Windows disponivel na tela "Ferramentas".</summary>
public sealed class WindowsToolItemViewModel : ItemViewModelBase
{
    public WindowsToolItemViewModel(WindowsToolDescriptor model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public WindowsToolDescriptor Model { get; }

    public string Id => Model.Id;

    public string Title => Localizer[Model.TitleKey];

    public string Description => Localizer[Model.DescriptionKey];

    public string Command => Model.Command;

    public string ArgumentsText => Model.DefaultArguments.Count == 0
        ? Localizer["Tools.NoArguments"]
        : string.Join(' ', Model.DefaultArguments);

    public ElevationRequirement Elevation => Model.Elevation;

    public string ElevationText => Localizer["Elevation." + Model.Elevation];

    public bool RequiresElevation => Model.Elevation is ElevationRequirement.Required;

    public bool IsAdvanced => Model.IsAdvanced;

    public string AdvancedText => Localizer["Tools.Advanced"];

    public string? Note => string.IsNullOrWhiteSpace(Model.NoteKey) ? null : Localizer[Model.NoteKey!];

    public bool HasNote => Note is not null;
}
