using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.ViewModels.Items;

/// <summary>Processo em execucao. As metricas sao atualizadas in-place.</summary>
public sealed class ProcessItemViewModel : ItemViewModelBase
{
    private ProcessInfoModel _model;

    public ProcessItemViewModel(ProcessInfoModel model, ILocalizer localizer)
        : base(localizer) => _model = model;

    public ProcessInfoModel Model => _model;

    public int Id => _model.Id;

    public string Name => _model.Name;

    public string Description => string.IsNullOrWhiteSpace(_model.Description) ? _model.Name : _model.Description;

    public string Company => string.IsNullOrWhiteSpace(_model.Company) ? Localizer["Process.UnknownPublisher"] : _model.Company;

    public string? FilePath => _model.FilePath;

    public bool HasPath => string.IsNullOrWhiteSpace(_model.FilePath) is false;

    public long MemoryBytes => _model.WorkingSetBytes;

    public string MemoryText => _model.MemoryText;

    public double CpuPercent => _model.CpuPercent;

    public string CpuText => _model.CpuText;

    public bool HasCpuActivity => _model.HasCpuActivity();

    public string DiskText => Humanize.Speed(_model.DiskBytesPerSecond);

    public string NetworkText => Humanize.Speed(_model.NetworkBytesPerSecond);

    public int ThreadCount => _model.ThreadCount;

    public int HandleCount => _model.HandleCount;

    public bool IsSystemCritical => _model.IsSystemCritical;

    public bool IsElevatedProcess => _model.IsElevatedProcess;

    public bool CanEnd => _model.IsSystemCritical is false;

    public string StartedText => _model.StartTimeUtc.HasValue
        ? Humanize.Date(_model.StartTimeUtc.Value.ToLocalTime())
        : Localizer["Common.NotAvailable"];

    public void Update(ProcessInfoModel model)
    {
        _model = model;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>Item de inicializacao do Windows.</summary>
public sealed class StartupItemViewModel : ItemViewModelBase
{
    public StartupItemViewModel(StartupEntry model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public StartupEntry Model { get; }

    public string Id => Model.Id;

    public string Name => Model.Name;

    public string Command => Model.Command;

    public string Publisher => string.IsNullOrWhiteSpace(Model.Publisher) ? Localizer["Startup.UnknownPublisher"] : Model.Publisher;

    public string LocationText => Localizer["Startup.Location." + Model.Location];

    public StartupLocation Location => Model.Location;

    public bool SupportsToggle => Model.SupportsToggle;

    public bool RequiresElevation => Model.RequiresElevation;

    public StartupImpact Impact => Model.Impact;

    public string ImpactText => Localizer["Startup.Impact." + Model.Impact];

    public string? ImpactReason => string.IsNullOrWhiteSpace(Model.ImpactReasonKey) ? null : Localizer[Model.ImpactReasonKey];

    public bool HasImpactReason => ImpactReason is not null;

    public bool HasSize => Model.ExecutableSizeBytes is > 0;

    public string SizeText => HasSize ? Humanize.Bytes(Model.ExecutableSizeBytes!.Value) : string.Empty;

    public bool IsEnabled
    {
        get => Model.IsEnabled;
        set
        {
            if (Model.IsEnabled == value)
            {
                return;
            }

            Model.IsEnabled = value;
            OnPropertyChanged();
        }
    }
}

/// <summary>Servico do Windows.</summary>
public sealed class ServiceItemViewModel : ItemViewModelBase
{
    public ServiceItemViewModel(WindowsServiceInfo model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public WindowsServiceInfo Model { get; }

    public string Name => Model.Name;

    public string DisplayName => Model.DisplayName;

    public string Description => string.IsNullOrWhiteSpace(Model.Description) ? Localizer["Services.NoDescription"] : Model.Description;

    public WindowsServiceState State => Model.State;

    public string StateText => Localizer["Services.State." + Model.State];

    public ServiceStartMode StartMode => Model.StartMode;

    public string StartModeText => Localizer["Services.StartMode." + Model.StartMode];

    public bool IsRunning => Model.State is WindowsServiceState.Running;

    public bool CanStop => Model.CanStop;

    public bool CanStart => Model.CanStop && Model.State is WindowsServiceState.Stopped;

    public bool CanChangeStartMode => Model.CanStop || Model.IsMicrosoft is false;

    public bool IsMicrosoft => Model.IsMicrosoft;

    public bool IsSystemCritical => Model.IsSystemCritical;

    public string? ExecutablePath => Model.ExecutablePath;

    public bool HasPath => string.IsNullOrWhiteSpace(Model.ExecutablePath) is false;

    public string OriginText => Model.IsMicrosoft ? Localizer["Services.Origin.Microsoft"] : Localizer["Services.Origin.ThirdParty"];
}

/// <summary>Plano de energia.</summary>
public sealed class PowerPlanItemViewModel : ItemViewModelBase
{
    public PowerPlanItemViewModel(PowerPlanInfo model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public PowerPlanInfo Model { get; }

    public string Name => Model.Name;

    public string Description => Model.IsWellKnown
        ? Localizer["Power.Kind." + Model.Kind + ".Detail"]
        : Localizer["Power.NoDescription"];

    public bool IsActive => Model.IsActive;

    public bool IsWellKnown => Model.IsWellKnown;

    public PowerPlanKind Kind => Model.Kind;

    public string KindText => Localizer["Power.Kind." + Model.Kind];

    public Guid SchemeGuid => Model.SchemeGuid;
}
