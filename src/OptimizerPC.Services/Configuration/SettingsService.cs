using System.Text.Json;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Configuration;

/// <summary>
/// Configuracoes do aplicativo em JSON, sempre no perfil local do usuario. A gravacao e
/// atomica (arquivo temporario + troca) para nunca deixar o arquivo pela metade.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private const string CorruptedSuffix = ".invalido";

    private readonly IAppPaths _paths;
    private readonly IAppLogger _logger;
    private readonly JsonSerializerOptions _options = AppJson.CreateOptions(indented: true);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsService(IAppPaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
        Current = new AppSettings();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            settings = await ReadAsync(cancellationToken).ConfigureAwait(false);
            Normalize(settings);
            Current = settings;
        }
        finally
        {
            _gate.Release();
        }

        RaiseChanged();
        return settings;
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Normalize(Current);
            await WriteAsync(Current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        RaiseChanged();
    }

    private async Task<AppSettings> ReadAsync(CancellationToken cancellationToken)
    {
        var path = _paths.SettingsFilePath;

        try
        {
            if (File.Exists(path) is false)
            {
                _logger.Info("Settings", "Nenhum arquivo de configuracoes encontrado; usando os padroes.");
                return new AppSettings();
            }

            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, _options, cancellationToken).ConfigureAwait(false);
            if (loaded is null)
            {
                _logger.Warning("Settings", "Arquivo de configuracoes vazio; usando os padroes.");
                return new AppSettings();
            }

            _logger.Info("Settings", "Configuracoes carregadas do perfil local.");
            return loaded;
        }
        catch (JsonException ex)
        {
            _logger.Warning("Settings", "Arquivo de configuracoes invalido; os padroes serao usados.", ex);
            PreserveCorruptedFile(path);
            return new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.Error("Settings", "Falha ao carregar as configuracoes; usando os padroes.", ex);
            return new AppSettings();
        }
    }

    private async Task WriteAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var path = _paths.SettingsFilePath;
        var temporary = path + ".tmp";

        try
        {
            _paths.EnsureCreated();
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, settings, _options, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite: true);
            _logger.Debug("Settings", "Configuracoes gravadas.");
        }
        catch (Exception ex)
        {
            _logger.Error("Settings", "Falha ao gravar as configuracoes.", ex);

            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Mantem uma copia do arquivo invalido para diagnostico, sem sobrescrever a anterior.</summary>
    private void PreserveCorruptedFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Move(path, path + CorruptedSuffix, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug("Settings", "Nao foi possivel preservar o arquivo de configuracoes invalido: " + ex.Message);
        }
    }

    /// <summary>Valores lidos de um arquivo editado a mao sao ajustados para faixas seguras.</summary>
    private void Normalize(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ReportsFolder))
        {
            settings.ReportsFolder = _paths.ReportsFolder;
        }

        if (string.IsNullOrWhiteSpace(settings.LogsFolder))
        {
            settings.LogsFolder = _paths.LogsFolder;
        }

        settings.MonitoringIntervalMs = settings.MonitoringIntervalMs is 500 or 1000 or 2000 or 5000
            ? settings.MonitoringIntervalMs
            : 1000;

        settings.LogRetentionDays = Math.Clamp(settings.LogRetentionDays, 1, 365);
        settings.Schedule.DayOfMonth = Math.Clamp(settings.Schedule.DayOfMonth, 1, 28);
        settings.Schedule.TimeOfDay = NormalizeTime(settings.Schedule.TimeOfDay);
        settings.Schedule.TaskName = string.IsNullOrWhiteSpace(settings.Schedule.TaskName)
            ? "OptimizerPC-Manutencao"
            : settings.Schedule.TaskName;
    }

    private static string NormalizeTime(string? value)
    {
        if (TimeOnly.TryParse(value, out var parsed))
        {
            return parsed.ToString("HH:mm");
        }

        return "12:00";
    }

    private void RaiseChanged()
    {
        var handler = SettingsChanged;
        handler?.Invoke(this, Current);
    }
}
