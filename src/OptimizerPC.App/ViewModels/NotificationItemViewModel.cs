using CommunityToolkit.Mvvm.ComponentModel;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.ViewModels;

/// <summary>
/// Notificacao exibida no sino da barra superior.
/// </summary>
public sealed class NotificationItemViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;

    public NotificationItemViewModel(AppNotification model, ILocalizer localizer)
    {
        Model = model;
        _localizer = localizer;
    }

    public AppNotification Model { get; }

    public string Id => Model.Id;

    public string Title => _localizer[Model.TitleKey];

    public string Message => _localizer[Model.MessageKey];

    public string? Detail => Model.Detail;

    public bool HasDetail => string.IsNullOrWhiteSpace(Model.Detail) is false;

    public NotificationSeverity Severity => Model.Severity;

    public bool IsRead => Model.IsRead;

    public string CreatedText => Humanize.Date(Model.CreatedAtUtc.ToLocalTime());

    public string? NavigationTarget => Model.NavigationTarget;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(CreatedText));
        OnPropertyChanged(nameof(IsRead));
    }
}
