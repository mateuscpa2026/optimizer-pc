using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizerPC.App.Services;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.ViewModels;

/// <summary>
/// Casca do aplicativo: navegacao lateral, barra superior, sino de notificacoes
/// e barra de status com o monitor em tempo real.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private const int HistoryCapacity = 60;

    private readonly INavigationService _navigation;
    private readonly IMonitoringService _monitoring;
    private readonly INotificationService _notifications;
    private readonly IElevationService _elevation;
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IDialogService _dialogs;

    private bool _isMonitoringSubscribed;
    private bool _isShuttingDown;

    public ShellViewModel(
        INavigationService navigation,
        IMonitoringService monitoring,
        INotificationService notifications,
        IElevationService elevation,
        ISettingsService settings,
        IThemeService theme,
        IDialogService dialogs,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _navigation = navigation;
        _monitoring = monitoring;
        _notifications = notifications;
        _elevation = elevation;
        _settings = settings;
        _theme = theme;
        _dialogs = dialogs;

        Sections = new ObservableCollection<NavigationSectionViewModel>();
        Notifications = new ObservableCollection<NotificationItemViewModel>();

        BuildNavigation();

        _navigation.Navigated += OnNavigated;
        _notifications.RecentChanged += OnNotificationsChanged;
        _notifications.NotificationPublished += OnNotificationPublished;
        _theme.ThemeChanged += OnThemeChanged;

        VersionText = ResolveVersion();
    }

    public ObservableCollection<NavigationSectionViewModel> Sections { get; }

    public ObservableCollection<NotificationItemViewModel> Notifications { get; }

    public ObservableCollection<double> CpuHistory { get; } = new();

    public ObservableCollection<double> MemoryHistory { get; } = new();

    public ObservableCollection<double> DiskHistory { get; } = new();

    /// <summary>ViewModel da tela atual; a View correspondente vem dos DataTemplates do App.xaml.</summary>
    [ObservableProperty]
    private ViewModelBase? _currentScreen;

    [ObservableProperty]
    private string _currentTitle = string.Empty;

    [ObservableProperty]
    private string _currentSubtitle = string.Empty;

    [ObservableProperty]
    private bool _isNotificationsOpen;

    [ObservableProperty]
    private bool _isSidebarCompact;

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private double _memoryPercent;

    [ObservableProperty]
    private double _diskPercent;

    [ObservableProperty]
    private int _processCount;

    [ObservableProperty]
    private string _memoryUsedText = string.Empty;

    [ObservableProperty]
    private bool _isMonitoring;

    [ObservableProperty]
    private bool _isDiskActivityAvailable;

    [ObservableProperty]
    private int _unreadCount;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private string _clockText = string.Empty;

    public string VersionText { get; }

    public bool IsElevated => _elevation.IsElevated;

    public bool HasUnread => UnreadCount > 0;

    public bool HasNotifications => Notifications.Count > 0;

    public string ElevationText => Localizer[IsElevated ? "Shell.Elevation.Admin" : "Shell.Elevation.Standard"];

    public string ProcessCountText => Localizer.Format("Shell.Status.Processes", ProcessCount);

    /// <summary>Rotulo honesto: o indice de saude e uma estimativa interna, nao um diagnostico.</summary>
    public string HealthDisclaimer => Localizer["Health.Disclaimer"];

    partial void OnUnreadCountChanged(int value) => OnPropertyChanged(nameof(HasUnread));

    partial void OnProcessCountChanged(int value) => OnPropertyChanged(nameof(ProcessCountText));

    partial void OnIsSidebarCompactChanged(bool value) => OnPropertyChanged(nameof(SidebarWidth));

    public double SidebarWidth => IsSidebarCompact ? 68d : 248d;

    protected override async Task OnInitializeAsync()
    {
        IsDarkTheme = _theme.IsDark;
        IsSidebarCompact = false;
        UpdateClock();

        _monitoring.Interval = TimeSpan.FromMilliseconds(_settings.Current.MonitoringIntervalOrDefault);
        SubscribeMonitoring();
        await LoadNotificationsAsync().ConfigureAwait(true);
        await _navigation.NavigateAsync(Screen.Dashboard).ConfigureAwait(true);
    }

    protected override async Task OnNavigatedToAsync()
    {
        if (_monitoring.IsRunning is false)
        {
            _monitoring.Start();
            IsMonitoring = true;
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }

    protected override void OnLanguageChanged()
    {
        UpdateClock();
        OnPropertyChanged(nameof(ElevationText));
        OnPropertyChanged(nameof(HealthDisclaimer));

        foreach (var section in Sections)
        {
            foreach (var item in section.Items)
            {
                item.RefreshTexts();
            }
        }

        foreach (var notification in Notifications)
        {
            notification.Refresh();
        }

        UpdateHeader(_navigation.CurrentScreen);
    }

    protected override void OnDispose()
    {
        UnsubscribeMonitoring();
        _navigation.Navigated -= OnNavigated;
        _notifications.RecentChanged -= OnNotificationsChanged;
        _notifications.NotificationPublished -= OnNotificationPublished;
        _theme.ThemeChanged -= OnThemeChanged;
    }

    private void BuildNavigation()
    {
        Sections.Clear();

        foreach (var section in NavigationCatalog.Sections)
        {
            var items = section.Entries
                .Select(entry => new NavigationItemViewModel(entry, Localizer, screen => _ = NavigateToAsync(screen)))
                .ToList();

            Sections.Add(new NavigationSectionViewModel(section.TitleKey, items, Localizer));
        }
    }

    private async Task NavigateToAsync(Screen screen)
    {
        IsNotificationsOpen = false;

        if (await _navigation.NavigateAsync(screen).ConfigureAwait(true) is false)
        {
            ReportError("Error.Navigation");
        }
    }

    [RelayCommand]
    private Task Navigate(Screen screen) => NavigateToAsync(screen);

    [RelayCommand]
    private void ToggleNotifications() => IsNotificationsOpen = IsNotificationsOpen is false;

    [RelayCommand]
    private void CloseNotifications() => IsNotificationsOpen = false;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCompact = IsSidebarCompact is false;

    [RelayCommand]
    private void ToggleTheme()
    {
        var target = _theme.IsDark ? ThemeMode.Light : ThemeMode.Dark;
        _theme.Apply(target);
        _settings.Current.Theme = target;

        if (_settings.Current.StartWithWindows || _settings.Current.HasCompletedFirstRun)
        {
            _ = PersistSettingsAsync();
        }
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        foreach (var notification in _notifications.Recent.ToList())
        {
            _notifications.MarkAsRead(notification.Id);
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }

    [RelayCommand]
    private void ClearNotifications() => _notifications.Clear();

    [RelayCommand]
    private async Task OpenNotificationAsync(NotificationItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _notifications.MarkAsRead(item.Id);
        IsNotificationsOpen = false;

        if (await _navigation.NavigateToTargetAsync(item.NavigationTarget).ConfigureAwait(true) is false)
        {
            await _dialogs.ShowInfoAsync("Dialog.Notification.Title", "Dialog.Notification.NoTarget", item.Message)
                .ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task OpenSystemInfoAsync()
    {
        var summary = string.Join(
            Environment.NewLine,
            Localizer.Format("Shell.About.Version", VersionText),
            Localizer.Format("Shell.About.Architecture", Environment.Is64BitOperatingSystem ? "64 bits" : "32 bits"),
            Localizer.Format("Shell.About.Processors", Environment.ProcessorCount),
            Localizer.Format("Shell.About.User", Environment.UserName),
            Localizer.Format("Shell.About.Runtime", Environment.Version.ToString()),
            Localizer["Shell.About.Privacy"]);

        await _dialogs.ShowInfoAsync("Shell.About.Title", "Shell.About.Message", summary).ConfigureAwait(true);
    }

    /// <summary>
    /// Decisao de fechamento: com "minimizar para a bandeja" ativo a janela some
    /// e o aplicativo continua monitorando. A confirmacao e sempre do usuario.
    /// </summary>
    public async Task<bool> RequestCloseAsync()
    {
        if (_isShuttingDown)
        {
            return true;
        }

        if (_settings.Current.MinimizeToTray is false)
        {
            _isShuttingDown = true;
            return true;
        }

        var minimize = await _dialogs
            .ConfirmActionAsync(
                "Dialog.Close.Title",
                "Dialog.Close.Message",
                "Dialog.Close.Minimize",
                detail: Localizer["Dialog.Close.Detail"])
            .ConfigureAwait(true);

        if (minimize)
        {
            return false;
        }

        _isShuttingDown = true;
        return true;
    }

    /// <summary>Encerra o aplicativo a partir do menu da bandeja.</summary>
    public void RequestShutdown() => _isShuttingDown = true;

    public void UpdateClock() => ClockText = Humanize.Date(DateTime.Now);

    private static string ResolveVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "1.0" : version.Major + "." + version.Minor;
    }

    private void OnNavigated(object? sender, Screen screen)
    {
        CurrentScreen = _navigation.CurrentViewModel;
        UpdateHeader(screen);
        SyncSelection(screen);
    }

    private void UpdateHeader(Screen screen)
    {
        var entry = NavigationCatalog.Find(screen);
        CurrentTitle = entry is null ? Localizer["Nav.Dashboard"] : Localizer[entry.TitleKey];
        CurrentSubtitle = Localizer["Screen.Subtitle." + screen];
    }

    private void SyncSelection(Screen screen)
    {
        foreach (var section in Sections)
        {
            foreach (var item in section.Items)
            {
                item.IsSelected = item.Screen == screen;
            }
        }
    }

    private void SubscribeMonitoring()
    {
        if (_isMonitoringSubscribed)
        {
            return;
        }

        _monitoring.SampleAvailable += OnSampleAvailable;
        _isMonitoringSubscribed = true;
    }

    private void UnsubscribeMonitoring()
    {
        if (_isMonitoringSubscribed is false)
        {
            return;
        }

        _monitoring.SampleAvailable -= OnSampleAvailable;
        _isMonitoringSubscribed = false;
    }

    private void OnSampleAvailable(object? sender, MetricSample sample)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            ApplySample(sample);
            return;
        }

        dispatcher.BeginInvoke(new Action(() => ApplySample(sample)));
    }

    private void ApplySample(MetricSample sample)
    {
        CpuPercent = Math.Round(sample.CpuPercent, 1);
        MemoryPercent = Math.Round(sample.MemoryPercent, 1);
        DiskPercent = sample.DiskActivityAvailable ? Math.Round(sample.DiskActivityPercent, 1) : 0;
        IsDiskActivityAvailable = sample.DiskActivityAvailable;
        ProcessCount = sample.ProcessCount;
        MemoryUsedText = Humanize.Bytes(sample.MemoryUsedBytes) + " / " + Humanize.Bytes(sample.MemoryTotalBytes);
        IsMonitoring = _monitoring.IsRunning;

        Append(CpuHistory, CpuPercent);
        Append(MemoryHistory, MemoryPercent);

        if (sample.DiskActivityAvailable)
        {
            Append(DiskHistory, DiskPercent);
        }
    }

    private static void Append(ObservableCollection<double> history, double value)
    {
        if (history.Count >= HistoryCapacity)
        {
            history.RemoveAt(0);
        }

        history.Add(value);
    }

    private async Task LoadNotificationsAsync()
    {
        var recent = _notifications.Recent;
        Notifications.Clear();

        foreach (var notification in recent)
        {
            Notifications.Add(new NotificationItemViewModel(notification, Localizer));
        }

        UnreadCount = Notifications.Count(item => item.IsRead is false);
        OnPropertyChanged(nameof(HasNotifications));
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private void OnNotificationsChanged(object? sender, EventArgs e) => _ = RefreshNotificationsAsync();

    private void OnNotificationPublished(object? sender, AppNotification notification)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            _ = RefreshNotificationsAsync();
            return;
        }

        dispatcher.BeginInvoke(new Action(() => _ = RefreshNotificationsAsync()));
    }

    private async Task RefreshNotificationsAsync()
    {
        await LoadNotificationsAsync().ConfigureAwait(true);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => IsDarkTheme = _theme.IsDark;

    private async Task PersistSettingsAsync()
    {
        try
        {
            await _settings.SaveAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Logger.Warning(nameof(ShellViewModel), "Nao foi possivel gravar as configuracoes: " + exception.Message);
        }
    }
}
