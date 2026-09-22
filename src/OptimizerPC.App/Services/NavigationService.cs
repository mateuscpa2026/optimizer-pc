using Microsoft.Extensions.DependencyInjection;
using OptimizerPC.App.ViewModels;
using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.App.Services;

public interface INavigationService
{
    Screen CurrentScreen { get; }

    ViewModelBase? CurrentViewModel { get; }

    event EventHandler<Screen>? Navigated;

    /// <summary>
    /// Troca a tela exibida. Devolve falso quando a tela nao esta registrada
    /// ou o ViewModel correspondente nao pudo ser criado.
    /// </summary>
    Task<bool> NavigateAsync(Screen screen);

    /// <summary>Abre a tela correspondente a um destino textual (usado pelas notificacoes).</summary>
    Task<bool> NavigateToTargetAsync(string? target);
}

/// <summary>
/// Resolve as telas pelo catalogo interno e mantem a tela atual. Os ViewModels
/// sao singletons: o estado da tela e preservado ao alternar entre elas.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;
    private readonly IAppLogger _logger;

    private ViewModelBase? _currentViewModel;
    private Screen? _currentScreen;

    public NavigationService(IServiceProvider services, IAppLogger logger)
    {
        _services = services;
        _logger = logger;
    }

    public Screen CurrentScreen => _currentScreen ?? Screen.Dashboard;

    public ViewModelBase? CurrentViewModel => _currentViewModel;

    public event EventHandler<Screen>? Navigated;

    public async Task<bool> NavigateAsync(Screen screen)
    {
        var entry = NavigationCatalog.Find(screen);
        if (entry is null)
        {
            _logger.Warning("Navigation", "Tela nao registrada no catalogo: " + screen + ".");
            return false;
        }

        if (_currentScreen == screen && _currentViewModel is not null)
        {
            return true;
        }

        ViewModelBase viewModel;
        try
        {
            viewModel = (ViewModelBase)_services.GetRequiredService(entry.ViewModelType);
        }
        catch (Exception exception)
        {
            _logger.Error("Navigation", "Nao foi possivel abrir a tela " + screen + ".", exception);
            return false;
        }

        if (_currentViewModel is not null)
        {
            await _currentViewModel.NotifyNavigatedFromAsync().ConfigureAwait(true);
        }

        _currentViewModel = viewModel;
        _currentScreen = screen;

        Navigated?.Invoke(this, screen);

        await viewModel.NotifyNavigatedToAsync().ConfigureAwait(true);
        return true;
    }

    public Task<bool> NavigateToTargetAsync(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(false);
        }

        foreach (var value in Enum.GetValues<Screen>())
        {
            if (string.Equals(value.ToString(), target.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return NavigateAsync(value);
            }
        }

        return Task.FromResult(false);
    }
}
