using System.Windows;
using OptimizerPC.App.ViewModels;

namespace OptimizerPC.App.Views;

/// <summary>
/// Janela de primeira execucao. Conduz o usuario pela deteccao do hardware e
/// pela primeira analise antes de liberar o aplicativo. A janela nao decide
/// nada: apenas acompanha o estado do ViewModel e devolve a escolha do usuario.
/// </summary>
public partial class FirstRunWindow : Window
{
    private readonly FirstRunViewModel _viewModel;

    public FirstRunWindow(FirstRunViewModel viewModel)
    {
        _viewModel = viewModel;

        InitializeComponent();

        DataContext = viewModel;

        _viewModel.Finished += OnFinished;
    }

    public FirstRunViewModel ViewModel => _viewModel;

    /// <summary>Indica se o usuario pediu para abrir o diagnostico ao sair.</summary>
    public bool StartDiagnosisRequested { get; private set; }

    private void OnFinished(object? sender, bool startDiagnosis)
    {
        StartDiagnosisRequested = startDiagnosis;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Finished -= OnFinished;
        base.OnClosed(e);
    }

    private async void OnCloseClick(object sender, RoutedEventArgs e) => await _viewModel.CloseAsync(startDiagnosis: false);
}
