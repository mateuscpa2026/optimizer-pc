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
    /// Encerra o aplicativo de fato: para o monitoramento, remove o icone da bandeja,
    /// fecha a janela e desliga o <see cref="Application"/>. Como o ShutdownMode e
    /// OnExplicitShutdown, nada aqui permanece vivo depois deste metodo.
    /// </summary>
    public void RequestExit()
    {
        if (_shutdownConfirmed)
        {
            return;
        }

        _shutdownConfirmed = true;

        _viewModel.RequestShutdown();
        _tray.Hide();

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

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (e.Cancel || _shutdownConfirmed)
        {
            return;
        }

        // O botao fechar encerra tudo: nao ha mais "minimizar para a bandeja" aqui.
        // Cancelamos o fechamento padrao apenas para executar a saida completa.
        e.Cancel = true;
        RequestExit();
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
        _tray.Dispose();
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

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        // Minimizar para a bandeja e opcional (Configuracoes). O botao fechar, nao:
        // ele sempre encerra o aplicativo por completo.
        if (_viewModel.MinimizeToTray)
        {
            Hide();
            _tray.Show();
            _tray.Notify(_localizer["Tray.Minimized.Title"], _localizer["Tray.Minimized.Message"]);
            return;
        }

        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
