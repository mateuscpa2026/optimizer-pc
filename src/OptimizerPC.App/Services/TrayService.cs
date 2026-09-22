using System.Windows;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace OptimizerPC.App.Services;

/// <summary>
/// Icone na area de notificacao. Permite manter o aplicativo em segundo plano
/// sem deixar a janela aberta.
/// </summary>
public interface ITrayService : IDisposable
{
    bool IsSupported { get; }

    void Show();

    void Hide();

    void Notify(string title, string message);

    event EventHandler? OpenRequested;

    event EventHandler? ExitRequested;
}

public sealed class TrayService : ITrayService
{
    private readonly ILocalizer _localizer;
    private readonly IAppLogger _logger;
    private WinForms.NotifyIcon? _icon;
    private WinForms.ToolStripMenuItem? _openItem;
    private WinForms.ToolStripMenuItem? _exitItem;
    private bool _disposed;

    public TrayService(ILocalizer localizer, IAppLogger logger)
    {
        _localizer = localizer;
        _logger = logger;
    }

    public bool IsSupported => true;

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public void Show()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _icon ??= CreateIcon();
            if (_icon is null)
            {
                return;
            }

            RefreshTexts();
            _icon.Visible = true;
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(TrayService), "Nao foi possivel exibir o icone na area de notificacao.", ex);
        }
    }

    public void Hide()
    {
        if (_icon is null)
        {
            return;
        }

        _icon.Visible = false;
    }

    public void Notify(string title, string message)
    {
        if (_icon is null || _icon.Visible is false)
        {
            return;
        }

        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = message;
            _icon.ShowBalloonTip(4000);
        }
        catch (Exception ex)
        {
            _logger.Debug(nameof(TrayService), "Aviso da area de notificacao nao exibido: " + ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }

    private WinForms.NotifyIcon? CreateIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        _openItem = new WinForms.ToolStripMenuItem();
        _exitItem = new WinForms.ToolStripMenuItem();

        _openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(_openItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_exitItem);

        var icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = false
        };

        icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _localizer.LanguageChanged += OnLanguageChanged;

        return icon;
    }

    private void OnLanguageChanged(object? sender, AppLanguage language) => RefreshTexts();

    private void RefreshTexts()
    {
        if (_icon is null)
        {
            return;
        }

        var name = _localizer["App.Title"];
        _icon.Text = name.Length > 62 ? name[..62] : name;

        if (_openItem is not null)
        {
            _openItem.Text = _localizer["Shell.Tray.Show"];
        }

        if (_exitItem is not null)
        {
            _exitItem.Text = _localizer["Shell.Tray.Exit"];
        }
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("Assets/OptimizerPC.ico", UriKind.Relative));
            if (resource?.Stream is not null)
            {
                using var stream = resource.Stream;
                return new Drawing.Icon(stream, new Drawing.Size(16, 16));
            }
        }
        catch
        {
            // O icone embutido nao esta acessivel: o icone padrao do sistema e usado.
        }

        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path) is false)
            {
                var extracted = Drawing.Icon.ExtractAssociatedIcon(path);
                if (extracted is not null)
                {
                    return extracted;
                }
            }
        }
        catch
        {
            // Sem icone associado: segue para o icone generico.
        }

        return Drawing.SystemIcons.Application;
    }
}
