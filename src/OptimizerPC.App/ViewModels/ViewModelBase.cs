using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.App.ViewModels;

/// <summary>
/// Base dos ViewModels. Concentra estado de execucao (ocupado, progresso, erro),
/// cancelamento e atualizacao dos textos quando o idioma muda.
/// Nenhum ViewModel toca o sistema no construtor: leituras acontecem em
/// <see cref="OnNavigatedToAsync"/>, sempre fora da thread de interface.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private bool _isBusy;
    private bool _isInitialized;
    private bool _isDisposed;
    private string _busyText = string.Empty;
    private string? _errorKey;
    private string _statusText = string.Empty;
    private Severity _statusSeverity = Severity.Ok;
    private double _progressPercent;
    private bool _isProgressIndeterminate;

    protected ViewModelBase(ILocalizer localizer, IAppLogger logger)
    {
        Localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Localizer.LanguageChanged += OnLanguageChangedInternal;
    }

    protected ILocalizer Localizer { get; }

    protected IAppLogger Logger { get; }

    protected CancellationToken LifetimeToken => _lifetime.Token;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => _isBusy is false;

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    /// <summary>Chave do ultimo erro; a interface traduz. Nulo quando nao ha erro.</summary>
    public string? ErrorKey
    {
        get => _errorKey;
        private set
        {
            if (SetProperty(ref _errorKey, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(ErrorMessage));
            }
        }
    }

    public bool HasError => string.IsNullOrEmpty(_errorKey) is false;

    public string ErrorMessage => string.IsNullOrEmpty(_errorKey) ? string.Empty : Localizer[_errorKey];

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => string.IsNullOrEmpty(_statusText) is false;

    public Severity StatusSeverity
    {
        get => _statusSeverity;
        private set => SetProperty(ref _statusSeverity, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, Math.Clamp(value, 0d, 100d));
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    /// <summary>
    /// Executa uma operacao de interface com estado de ocupado, cancelamento e
    /// tratamento de erro local. Devolve falso quando a operacao falhou.
    /// </summary>
    protected async Task<bool> RunAsync(Func<CancellationToken, Task> work, string busyTextKey = "")
    {
        ArgumentNullException.ThrowIfNull(work);

        if (IsBusy)
        {
            return false;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = linked;

        IsBusy = true;
        ErrorKey = null;
        BusyText = string.IsNullOrEmpty(busyTextKey) ? string.Empty : Localizer[busyTextKey];

        try
        {
            await work(linked.Token).ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText = string.Empty;
            return false;
        }
        catch (Exception exception)
        {
            Logger.Error(GetType().Name, "Falha em operacao de interface.", exception);
            ErrorKey = "Error.Unexpected";
            return false;
        }
        finally
        {
            _operation = null;
            IsBusy = false;
            BusyText = string.Empty;
            IsProgressIndeterminate = false;
            ProgressPercent = 0;
        }
    }

    /// <summary>Variante que devolve o resultado da operacao.</summary>
    protected async Task<(bool Success, T? Value)> RunAsync<T>(
        Func<CancellationToken, Task<T>> work,
        string busyTextKey = "")
    {
        ArgumentNullException.ThrowIfNull(work);

        T? value = default;
        var success = await RunAsync(async token =>
        {
            value = await work(token).ConfigureAwait(true);
        }, busyTextKey).ConfigureAwait(true);

        return (success, value);
    }

    protected void SetProgress(double percent, string? textKey = null, params object[] args)
    {
        IsProgressIndeterminate = false;
        ProgressPercent = percent;

        if (string.IsNullOrEmpty(textKey))
        {
            return;
        }

        BusyText = args.Length == 0 ? Localizer[textKey] : Localizer.Format(textKey, args);
    }

    protected void SetStatus(string key, Severity severity = Severity.Ok)
    {
        StatusSeverity = severity;
        StatusText = string.IsNullOrEmpty(key) ? string.Empty : Localizer[key];
    }

    protected void SetStatusFormat(string key, Severity severity, params object[] args)
    {
        StatusSeverity = severity;
        StatusText = Localizer.Format(key, args);
    }

    protected void ClearStatus()
    {
        StatusSeverity = Severity.Ok;
        StatusText = string.Empty;
    }

    protected void ReportError(string key)
    {
        ErrorKey = key;
    }

    protected void ClearError() => ErrorKey = null;

    /// <summary>
    /// Preparacao executada uma unica vez, na primeira navegacao ate a tela.
    /// </summary>
    public async Task EnsureInitializedAsync()
    {
        if (_isInitialized || _isDisposed)
        {
            return;
        }

        _isInitialized = true;

        try
        {
            await OnInitializeAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Encerramento.
        }
        catch (Exception exception)
        {
            Logger.Error(GetType().Name, "Falha na inicializacao da tela.", exception);
            ReportError("Error.Unexpected");
        }
    }

    /// <summary>Chamado pelo shell sempre que a tela entra em exibicao.</summary>
    public async Task NotifyNavigatedToAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await EnsureInitializedAsync().ConfigureAwait(true);

        try
        {
            await OnNavigatedToAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Navegacao interrompida.
        }
        catch (Exception exception)
        {
            Logger.Error(GetType().Name, "Falha ao ativar a tela.", exception);
            ReportError("Error.Unexpected");
        }
    }

    /// <summary>Chamado pelo shell quando a tela deixa de ser exibida.</summary>
    public async Task NotifyNavigatedFromAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            await OnNavigatedFromAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Logger.Warning(GetType().Name, "Falha ao desativar a tela: " + exception.Message);
        }
    }

    protected virtual Task OnInitializeAsync() => Task.CompletedTask;

    protected virtual Task OnNavigatedToAsync() => Task.CompletedTask;

    protected virtual Task OnNavigatedFromAsync() => Task.CompletedTask;

    /// <summary>
    /// Ponto de extensao para os ViewModels que precisam reconsultar fontes do
    /// sistema em segundo plano enquanto a tela esta visivel.
    /// </summary>
    protected virtual void OnLanguageChanged()
    {
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operation?.Cancel();

    private bool CanCancel() => IsBusy;

    private void OnLanguageChangedInternal(object? sender, AppLanguage language)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RefreshLocalizedText();
            return;
        }

        dispatcher.BeginInvoke(new Action(RefreshLocalizedText));
    }

    private void RefreshLocalizedText()
    {
        if (_isDisposed)
        {
            return;
        }

        OnLanguageChanged();
        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Localizer.LanguageChanged -= OnLanguageChangedInternal;

        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Já liberado.
        }

        OnDispose();

        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    protected virtual void OnDispose()
    {
    }
}
