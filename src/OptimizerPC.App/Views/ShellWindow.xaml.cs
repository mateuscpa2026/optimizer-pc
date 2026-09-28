using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
    private bool _isClosing;
    private bool _isClosed;

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

        // Se ja estamos dentro de OnClosing, nao chamamos Close() de novo (lançaria
        // InvalidOperationException); o fechamento natural prossegue e o OnClosed desliga.
        if (_isClosing)
        {
            return;
        }

        Close();
        Application.Current?.Shutdown();
    }

    /// <summary>Sobe a janela para o primeiro plano, restaurando o estado minimizado.</summary>
    public void BringToFront()
    {
        if (_isClosed)
        {
            return;
        }

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
        // Marcamos a saida, limpamos bandeja/monitoramento e deixamos o fechamento
        // natural prosseguir; o OnClosed faz o Shutdown do Application.
        _isClosing = true;
        _shutdownConfirmed = true;
        _viewModel.RequestShutdown();
        _tray.Hide();
        e.Cancel = false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateMaximizeIcon();
        ApplySidebarMode(ActualWidth);
        ViewModel.UpdateClock();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;

        _tray.OpenRequested -= OnTrayOpenRequested;
        _tray.ExitRequested -= OnTrayExitRequested;
        StateChanged -= OnStateChanged;
        SizeChanged -= OnSizeChanged;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        _tray.Dispose();
        ViewModel.Dispose();

        // Garante que o processo termine de fato (ShutdownMode e OnExplicitShutdown),
        // liberando o mutex de instancia unica para uma reabertura limpa.
        Application.Current?.Shutdown();
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

    // Janela borderless maximizada (WindowStyle=None + WindowChrome) transborda a
    // area de trabalho: cobre a barra de tarefas e corta ~7px das bordas. Este
    // handler restringe o tamanho/posicao maximizados ao work area do monitor.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            ApplyMaxSizeToWorkArea(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void ApplyMaxSizeToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

            if (GetMonitorInfo(monitor, ref info))
            {
                var work = info.rcWork;
                var full = info.rcMonitor;
                mmi.ptMaxPosition.x = work.left - full.left;
                mmi.ptMaxPosition.y = work.top - full.top;
                mmi.ptMaxSize.x = work.right - work.left;
                mmi.ptMaxSize.y = work.bottom - work.top;
            }
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
