using System.Windows;
using System.Windows.Media;
using OptimizerPC.Core;

namespace OptimizerPC.App.Views;

public enum DialogKind
{
    Info = 0,
    Question = 1,
    Warning = 2,
    Error = 3,
    Success = 4
}

/// <summary>
/// Janela de aviso do aplicativo. Substitui o MessageBox do sistema para manter
/// o mesmo visual nos dois temas e o texto no idioma escolhido.
/// </summary>
public partial class MessageDialogWindow : Window
{
    public MessageDialogWindow()
    {
        InitializeComponent();
    }

    public static bool? Show(
        Window? owner,
        DialogKind kind,
        string title,
        string message,
        string detail,
        string primaryText,
        string? secondaryText)
    {
        var window = new MessageDialogWindow
        {
            Owner = owner ?? Application.Current?.MainWindow
        };

        window.Apply(kind, title, message, detail, primaryText, secondaryText);
        return window.ShowDialog();
    }

    private void Apply(DialogKind kind, string title, string message, string detail, string primaryText, string? secondaryText)
    {
        TitleText.Text = title;
        MessageText.Text = message;

        if (string.IsNullOrWhiteSpace(detail))
        {
            DetailHost.Visibility = Visibility.Collapsed;
        }
        else
        {
            DetailHost.Visibility = Visibility.Visible;
            DetailText.Text = detail;
        }

        PrimaryButton.Content = primaryText;
        PrimaryButton.IsDefault = true;

        if (string.IsNullOrWhiteSpace(secondaryText))
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
            PrimaryButton.IsCancel = true;
        }
        else
        {
            SecondaryButton.Content = secondaryText;
            SecondaryButton.IsCancel = true;
        }

        IconPath.Data = (Geometry)(Application.Current?.TryFindResource(IconKey(kind)) ?? IconPath.Data);
        IconPath.Stroke = Brush(kind, "Brush.Accent");
        IconHost.Background = Brush(kind, "Brush.Accent.Soft");
    }

    private static string IconKey(DialogKind kind) => kind switch
    {
        DialogKind.Question => "Icon.Question",
        DialogKind.Warning => "Icon.Warning",
        DialogKind.Error => "Icon.Warning",
        DialogKind.Success => "Icon.Check",
        _ => "Icon.Info"
    };

    private static Brush Brush(DialogKind kind, string accentKey) => (Brush)(Application.Current?.TryFindResource(kind switch
    {
        DialogKind.Question => accentKey,
        DialogKind.Warning => accentKey.Replace("Brush.Accent", "Brush.Warning", StringComparison.Ordinal),
        DialogKind.Error => accentKey.Replace("Brush.Accent", "Brush.Danger", StringComparison.Ordinal),
        DialogKind.Success => accentKey.Replace("Brush.Accent", "Brush.Success", StringComparison.Ordinal),
        _ => accentKey
    }) ?? Brushes.Transparent);

    private void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnSecondaryClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
