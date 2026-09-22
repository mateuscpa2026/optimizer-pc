using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.System;

/// <summary>
/// Monitor continuo. Publica amostras em intervalo configuravel e mantem um pequeno
/// historico em memoria para os graficos da tela de desempenho.
/// </summary>
public sealed class MonitoringService : IMonitoringService
{
    private const int MaxHistorySamples = 180;

    private readonly IMetricsProvider _provider;
    private readonly IAppLogger _logger;
    private readonly object _sync = new();
    private readonly Queue<MetricSample> _history = new();

    private Timer? _timer;
    private int _sampling;
    private TimeSpan _interval = TimeSpan.FromSeconds(1);
    private bool _disposed;

    public MonitoringService(IMetricsProvider provider, IAppLogger logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public event EventHandler<MetricSample>? SampleAvailable;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _timer is not null;
            }
        }
    }

    public TimeSpan Interval
    {
        get
        {
            lock (_sync)
            {
                return _interval;
            }
        }

        set
        {
            var normalized = value < TimeSpan.FromMilliseconds(250) ? TimeSpan.FromMilliseconds(250) : value;

            lock (_sync)
            {
                _interval = normalized;

                if (_timer is not null)
                {
                    _timer.Change(TimeSpan.Zero, normalized);
                }
            }
        }
    }

    public MetricSample? LastSample { get; private set; }

    /// <summary>Amostras mais recentes, da mais antiga para a mais nova.</summary>
    public IReadOnlyList<MetricSample> GetHistory()
    {
        lock (_sync)
        {
            return _history.ToArray();
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_disposed || _timer is not null)
            {
                return;
            }

            _provider.Reset();
            _timer = new Timer(OnTick, null, TimeSpan.Zero, _interval);
        }

        _logger.Debug("Monitoring", "Monitoramento iniciado com intervalo de " + _interval.TotalMilliseconds + " ms.");
    }

    public void Stop()
    {
        Timer? timer;

        lock (_sync)
        {
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();

        if (timer is not null)
        {
            _logger.Debug("Monitoring", "Monitoramento interrompido.");
        }
    }

    private void OnTick(object? state)
    {
        // Evita acumular amostras quando uma leitura demora mais que o intervalo.
        if (Interlocked.Exchange(ref _sampling, 1) == 1)
        {
            return;
        }

        try
        {
            var sample = _provider.SampleAsync().GetAwaiter().GetResult();

            lock (_sync)
            {
                LastSample = sample;
                _history.Enqueue(sample);

                while (_history.Count > MaxHistorySamples)
                {
                    _history.Dequeue();
                }
            }

            SampleAvailable?.Invoke(this, sample);
        }
        catch (Exception exception)
        {
            _logger.Warning("Monitoring", "Falha ao coletar a amostra de desempenho.", exception);
        }
        finally
        {
            Interlocked.Exchange(ref _sampling, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
