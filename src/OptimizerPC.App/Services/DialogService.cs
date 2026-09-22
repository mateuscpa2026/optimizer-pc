using System.Windows;
using Microsoft.Win32;
using OptimizerPC.App.Views;
using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.App.Services;

/// <summary>
/// Avisos e confirmacoes do aplicativo. Nada e executado sem passar por aqui
/// quando a acao exige confirmacao.
/// </summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string titleKey, string messageKey, string? detail = null, DialogKind kind = DialogKind.Question);

    Task<bool> ConfirmActionAsync(string titleKey, string messageKey, string confirmKey, string? detail = null, DialogKind kind = DialogKind.Question);

    Task ShowInfoAsync(string titleKey, string messageKey, string? detail = null);

    Task ShowWarningAsync(string titleKey, string messageKey, string? detail = null);

    Task ShowErrorAsync(string titleKey, string messageKey, string? detail = null);

    Task ShowSuccessAsync(string titleKey, string messageKey, string? detail = null);

    string? AskSaveFile(string suggestedFileName, string filter, string titleKey);
}

public sealed class DialogService : IDialogService
{
    private readonly ILocalizer _localizer;

    public DialogService(ILocalizer localizer)
    {
        _localizer = localizer;
    }

    public Task<bool> ConfirmAsync(string titleKey, string messageKey, string? detail = null, DialogKind kind = DialogKind.Question)
        => ConfirmActionAsync(titleKey, messageKey, "Dialog.Yes", detail, kind);

    public Task<bool> ConfirmActionAsync(string titleKey, string messageKey, string confirmKey, string? detail = null, DialogKind kind = DialogKind.Question)
    {
        var result = MessageDialogWindow.Show(
            Owner(),
            kind,
            _localizer[titleKey],
            _localizer[messageKey],
            detail ?? string.Empty,
            _localizer[confirmKey],
            _localizer["Dialog.Cancel"]);

        return Task.FromResult(result == true);
    }

    public Task ShowInfoAsync(string titleKey, string messageKey, string? detail = null)
        => ShowAsync("Dialog.Title.Info", DialogKind.Info, titleKey, messageKey, detail);

    public Task ShowWarningAsync(string titleKey, string messageKey, string? detail = null)
        => ShowAsync("Dialog.Title.Warning", DialogKind.Warning, titleKey, messageKey, detail);

    public Task ShowErrorAsync(string titleKey, string messageKey, string? detail = null)
        => ShowAsync("Dialog.Title.Error", DialogKind.Error, titleKey, messageKey, detail);

    public Task ShowSuccessAsync(string titleKey, string messageKey, string? detail = null)
        => ShowAsync("Dialog.Title.Info", DialogKind.Success, titleKey, messageKey, detail);

    public string? AskSaveFile(string suggestedFileName, string filter, string titleKey)
    {
        var dialog = new SaveFileDialog
        {
            FileName = suggestedFileName,
            Filter = filter,
            Title = _localizer[titleKey],
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
    }

    private Task ShowAsync(string titleDefaultKey, DialogKind kind, string titleKey, string messageKey, string? detail)
    {
        var title = _localizer.TryGet(titleKey, out var custom) ? custom : _localizer[titleDefaultKey];

        MessageDialogWindow.Show(
            Owner(),
            kind,
            title,
            _localizer[messageKey],
            detail ?? string.Empty,
            _localizer["Dialog.Ok"],
            null);

        return Task.CompletedTask;
    }

    private static Window? Owner()
    {
        var application = Application.Current;
        if (application is null)
        {
            return null;
        }

        foreach (Window window in application.Windows)
        {
            if (window.IsActive)
            {
                return window;
            }
        }

        return application.MainWindow;
    }
}
