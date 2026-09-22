using System.Text;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Logging;

/// <summary>Destino de log. Permite combinar arquivo e banco sem duplicar a formatacao.</summary>
public interface ILogSink
{
    void Write(LogRecord record);
}

/// <summary>
/// Log em arquivo texto, um arquivo por dia, com rotacao por tamanho e limpeza por retencao.
/// Nunca registra senhas, tokens ou conteudo de arquivos pessoais.
/// </summary>
public sealed class FileLogSink : ILogSink
{
    private const long MaxFileBytes = 4L * 1024 * 1024;

    private readonly IAppPaths _paths;
    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly string _userProfile;

    public FileLogSink(IAppPaths paths, IClock clock)
    {
        _paths = paths;
        _clock = clock;
        _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public string CurrentLogFile => Path.Combine(_paths.LogsFolder, $"optimizerpc-{_clock.LocalNow:yyyyMMdd}.log");

    public void Write(LogRecord record)
    {
        try
        {
            lock (_gate)
            {
                _paths.EnsureCreated();
                var path = CurrentLogFile;
                Rotate(path);

                var line = new StringBuilder()
                    .Append(record.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [").Append(record.LevelText).Append(']')
                    .Append(" [").Append(record.Category).Append("] ")
                    .Append(Sanitize(record.Message))
                    .ToString();

                if (!string.IsNullOrWhiteSpace(record.Exception))
                {
                    line = line + Environment.NewLine + "    " + Sanitize(record.Exception!);
                }

                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Um log que falha nunca deve derrubar o aplicativo.
        }
    }

    /// <summary>Remove arquivos de log mais antigos que o periodo de retencao informado.</summary>
    public void ApplyRetention(int retentionDays)
    {
        if (retentionDays <= 0)
        {
            return;
        }

        try
        {
            var limit = _clock.LocalNow.Date.AddDays(-retentionDays);

            foreach (var file in Directory.EnumerateFiles(_paths.LogsFolder, "optimizerpc-*.log*"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < limit)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception)
                {
                    // Arquivo em uso: sera removido em uma proxima execucao.
                }
            }
        }
        catch (Exception)
        {
            // Pasta de logs indisponivel: nada a fazer.
        }
    }

    private static void Rotate(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxFileBytes)
            {
                return;
            }

            var archive = path + ".1";
            if (File.Exists(archive))
            {
                File.Delete(archive);
            }

            File.Move(path, archive);
        }
        catch (Exception)
        {
            // Sem rotacao quando o arquivo estiver bloqueado.
        }
    }

    /// <summary>Substitui o caminho do perfil do usuario para nao registrar nomes pessoais.</summary>
    private string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(_userProfile))
        {
            return text;
        }

        return text.Replace(_userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Grava os logs no banco local, respeitando o nivel minimo configurado.</summary>
public sealed class AppLogger : IAppLogger, IDisposable
{
    private readonly List<ILogSink> _sinks;
    private readonly object _gate = new();
    private LogLevel _minimumLevel;

    public AppLogger(LogLevel minimumLevel, params ILogSink[] sinks)
    {
        _minimumLevel = minimumLevel;
        _sinks = sinks.Where(s => s is not null).ToList();
    }

    public LogLevel MinimumLevel
    {
        get => _minimumLevel;
        set => _minimumLevel = value;
    }

    public void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        if (level < _minimumLevel)
        {
            return;
        }

        var record = new LogRecord
        {
            TimestampUtc = DateTime.UtcNow,
            Level = level,
            Category = category,
            Message = message,
            Exception = exception?.ToString()
        };

        List<ILogSink> sinks;
        lock (_gate)
        {
            sinks = _sinks.ToList();
        }

        foreach (var sink in sinks)
        {
            try
            {
                sink.Write(record);
            }
            catch (Exception)
            {
                // Falha em um destino de log nao interrompe o aplicativo nem os outros destinos.
            }
        }
    }

    public void AddSink(ILogSink sink)
    {
        lock (_gate)
        {
            _sinks.Add(sink);
        }
    }

    public void RemoveSink(ILogSink sink)
    {
        lock (_gate)
        {
            _sinks.Remove(sink);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _sinks.Clear();
        }
    }
}
