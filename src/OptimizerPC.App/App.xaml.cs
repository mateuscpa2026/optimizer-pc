using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using OptimizerPC.App.Localization;
using OptimizerPC.App.Services;
using OptimizerPC.App.ViewModels;
using OptimizerPC.App.Views;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Services.DependencyInjection;

namespace OptimizerPC.App;

/// <summary>
/// Ponto de entrada do aplicativo. Monta a injecao de dependencia, carrega as
/// configuracoes locais, aplica idioma e tema e abre a janela principal.
/// O modo <c>--maintenance</c> roda sem interface, disparado pela tarefa agendada.
/// </summary>
public partial class App : Application
{
    private const string MaintenanceArgument = "--maintenance";

    private ServiceProvider? _services;
    private SingleInstanceGuard? _guard;
    private ShellWindow? _shell;
    private bool _isMaintenance;
    private int _exitCode;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        RegisterGlobalHandlers();

        _isMaintenance = e.Args.Any(argument =>
            string.Equals(argument, MaintenanceArgument, StringComparison.OrdinalIgnoreCase));

        try
        {
            _services = BuildServices();
        }
        catch (Exception exception)
        {
            ShowStartupFailure(exception);
            _exitCode = 1;
            Shutdown(1);
            return;
        }

        _services.ApplyLoggingPreferences();

        if (_isMaintenance)
        {
            await RunMaintenanceAsync().ConfigureAwait(true);
            return;
        }

        _guard = new SingleInstanceGuard();
        if (_guard.IsPrimaryInstance is false)
        {
            _guard.SignalExistingInstance();
            _guard.Dispose();
            _guard = null;
            Shutdown(0);
            return;
        }

        await StartShellAsync().ConfigureAwait(true);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        _services = null;

        _guard?.Dispose();
        _guard = null;

        base.OnExit(e);

        // Rede de seguranca: depois de liberar servicos e bandeja, forcamos o fim do
        // processo para que nenhuma thread remanescente mantenha o aplicativo aberto.
        Environment.Exit(_exitCode);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddOptimizerPcServices();

        services.AddSingleton<AppLocalizer>();
        services.AddSingleton<ILocalizer>(provider => provider.GetRequiredService<AppLocalizer>());
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ITrayService, TrayService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<MaintenanceRunner>();
        services.AddSingleton<ToolLaunchState>();

        // Telas: uma instancia viva por tela, resolvida pelo NavigationCatalog.
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<DiagnosisViewModel>();
        services.AddSingleton<OptimizationViewModel>();
        services.AddSingleton<CleaningViewModel>();
        services.AddSingleton<StartupViewModel>();
        services.AddSingleton<ProcessesViewModel>();
        services.AddSingleton<ServicesViewModel>();
        services.AddSingleton<PerformanceViewModel>();
        services.AddSingleton<StorageViewModel>();
        services.AddSingleton<LowEndViewModel>();
        services.AddSingleton<GamerViewModel>();
        services.AddSingleton<ToolsViewModel>();
        services.AddSingleton<RestoreViewModel>();
        services.AddSingleton<ReportsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<FirstRunViewModel>();
        services.AddSingleton<FirstRunWindow>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ShellWindow>();

        return services.BuildServiceProvider();
    }

    private async Task StartShellAsync()
    {
        var services = _services!;
        var logger = services.GetRequiredService<IAppLogger>();
        var settings = services.GetRequiredService<ISettingsService>();
        var localizer = services.GetRequiredService<ILocalizer>();

        try
        {
            await settings.LoadAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            logger.Error("Startup", "Nao foi possivel carregar as configuracoes; os valores padrao serao usados.", exception);
        }

        localizer.SetLanguage(settings.Current.Language);
        LocalizationProxy.Current.Attach(localizer);

        OptimizerPC.App.Controls.Motion.Enabled = settings.Current.ReducedMotion is false;

        var theme = services.GetRequiredService<IThemeService>();
        theme.Apply(settings.Current.Theme);

        _shell = services.GetRequiredService<ShellWindow>();
        MainWindow = _shell;

        _shell.Show();
        _shell.BringToFront();

        _guard?.ListenToActivationRequests(() =>
            Dispatcher.BeginInvoke(new Action(() => _shell?.BringToFront())));

        await _shell.ViewModel.NotifyNavigatedToAsync().ConfigureAwait(true);

        logger.Info("Startup", "Optimizer PC iniciado. Versao " + _shell.ViewModel.VersionText + ".");

        await ShowFirstRunIfNeededAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Apresenta a janela de primeira execucao apenas uma vez. Tudo nela e somente
    /// leitura; se o usuario pedir, o aplicativo ja abre na tela de diagnostico.
    /// </summary>
    private async Task ShowFirstRunIfNeededAsync()
    {
        var services = _services!;
        var settings = services.GetRequiredService<ISettingsService>();

        if (settings.Current.HasCompletedFirstRun)
        {
            return;
        }

        try
        {
            var wizard = services.GetRequiredService<FirstRunWindow>();
            wizard.Owner = _shell;
            wizard.ShowDialog();

            if (wizard.StartDiagnosisRequested)
            {
                var navigation = services.GetRequiredService<INavigationService>();
                await navigation.NavigateAsync(Screen.Diagnosis).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            services.GetRequiredService<IAppLogger>()
                .Error("FirstRun", "A janela de primeira execucao nao pode ser exibida.", exception);
        }
    }

    private async Task RunMaintenanceAsync()
    {
        var services = _services!;
        var logger = services.GetRequiredService<IAppLogger>();
        var exitCode = 1;

        try
        {
            var runner = services.GetRequiredService<MaintenanceRunner>();
            exitCode = await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.Error("Maintenance", "A manutencao agendada terminou com erro nao tratado.", exception);
        }
        finally
        {
            _exitCode = exitCode;
            Shutdown(exitCode);
        }
    }

    private void RegisterGlobalHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("Falha nao tratada na interface.", e.Exception);
        e.Handled = true;

        if (e.Exception is OutOfMemoryException or StackOverflowException or AccessViolationException)
        {
            MessageBox.Show(
                "O aplicativo precisa ser encerrado devido a um erro grave. Os detalhes foram gravados no log.",
                "Optimizer PC",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            _exitCode = 1;
            Shutdown(1);
            return;
        }

        MessageBox.Show(
            "Ocorreu um erro inesperado. A operacao atual foi interrompida e os detalhes foram gravados no log.",
            "Optimizer PC",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => Log("Falha nao tratada no dominio do aplicativo.", e.ExceptionObject as Exception);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log("Falha nao observada em tarefa de segundo plano.", e.Exception);
        e.SetObserved();
    }

    private void Log(string message, Exception? exception)
    {
        try
        {
            _services?.GetService<IAppLogger>()?.Error("Crash", message, exception);
        }
        catch
        {
            // O log nao pode falhar em cima de uma falha: sem registro e melhor que novo erro.
        }
    }

    private void ShowStartupFailure(Exception exception)
    {
        var detail = exception.Message;

        try
        {
            var logFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OptimizerPC",
                "Logs");

            Directory.CreateDirectory(logFolder);

            var logPath = Path.Combine(logFolder, "inicializacao-falhou.log");
            File.AppendAllText(
                logPath,
                "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + exception + Environment.NewLine);

            detail += Environment.NewLine + Environment.NewLine + "Detalhes em: " + logPath;
        }
        catch
        {
            // Sem log de emergencia; a mensagem abaixo segue valendo.
        }

        MessageBox.Show(
            "Nao foi possivel iniciar o Optimizer PC." + Environment.NewLine + Environment.NewLine + detail,
            "Optimizer PC",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
