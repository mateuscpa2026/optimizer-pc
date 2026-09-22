using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using OptimizerPC.App.Services;
using OptimizerPC.App.ViewModels;
using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.App.Views;

/// <summary>
/// Janela principal. A logica fica no ShellViewModel; aqui apenas o que so a
/// janela sabe fazer: arrastar/redimensionar, alternar o estado da janela,
/// acompanhar o tamanho para compactar a barra lateral e reagir a bandeja.
/// </summary>
public partial class ShellWindow : Window
{
    private const double CompactSidebarThreshold = 1180d;

    private readonly ShellViewModel _viewModel;
    private readonly ITrayService _tray;
    private readonly ILocalizer _localizer;
    private bool _shutdownConfirmed;

    public ShellWindow(ShellViewModel viewModel, ITrayService tray, ILocalizer localizer)
    {
        _viewModel = viewModel;
        _tray = tray;
        _localizer = localizer;

        InitializeComponent();

        DataContext = viewModel;

        _tray.OpenRequested += OnTrayOpenRequested;
        _tray.ExitRequested += OnTrayExitRequested;

        StateChanged += OnStateChanged;
        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public ShellViewModel ViewModel => _viewModel;

    /// <summary>
    /// Encerra o aplicativo de fato. O fechamento normal da janela (botao X) e
    /// sempre interceptado para perguntar ao usuario; este caminho pula a pergunta.
    /// </summary>
    public void RequestExit()
    {
        _shutdownConfirmed = true;
        Close();
        Application.Current?.Shutdown();
    }

    /// <summary>Sobe a janela para o primeiro plano, restaurando o estado minimizado.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (e.Cancel)
        {
            return;
        }

        if (_shutdownConfirmed)
        {
            ViewModel.RequestShutdown();
            return;
        }

        // O fechamento direto e sempre interceptado: o usuario escolhe entre
        // minimizar para a bandeja e encerrar o aplicativo.
        e.Cancel = true;

        if (await ViewModel.RequestCloseAsync())
        {
            RequestExit();
            return;
        }

        Hide();
        _tray.Show();
        _tray.Notify(_localizer["Tray.Minimized.Title"], _localizer["Tray.Minimized.Message"]);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateMaximizeIcon();
        ApplySidebarMode(ActualWidth);
        ViewModel.UpdateClock();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _tray.OpenRequested -= OnTrayOpenRequested;
        _tray.ExitRequested -= OnTrayExitRequested;
        StateChanged -= OnStateChanged;
        SizeChanged -= OnSizeChanged;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        ViewModel.Dispose();
    }

    private void OnStateChanged(object? sender, EventArgs e) => UpdateMaximizeIcon();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            ApplySidebarMode(e.NewSize.Width);
        }
    }

    private void ApplySidebarMode(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var compact = width < CompactSidebarThreshold;
        if (ViewModel.IsSidebarCompact != compact)
        {
            ViewModel.IsSidebarCompact = compact;
        }
    }

    private void UpdateMaximizeIcon()
    {
        var key = WindowState == WindowState.Maximized ? "Icon.Window.Restore" : "Icon.Window.Maximize";

        if (TryFindResource(key) is Geometry geometry)
        {
            MaximizeIcon.Data = geometry;
        }
    }

    private void OnTrayOpenRequested(object? sender, EventArgs e) => BringToFront();

    private void OnTrayExitRequested(object? sender, EventArgs e) => RequestExit();

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
