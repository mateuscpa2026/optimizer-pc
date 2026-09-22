using System.Threading.Channels;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Storage;

namespace OptimizerPC.Services.Logging;

/// <summary>
/// Grava os logs no banco local sem bloquear a interface: as mensagens entram em uma
/// fila e sao persistidas em lotes. Uma falha de gravacao nunca derruba o aplicativo.
/// </summary>
public sealed class DatabaseLogSink : ILogSink, IAsyncDisposable
{
    private const int FlushBatchSize = 64;

    private readonly LogRepository _repository;
    private readonly Channel<LogRecord> _channel;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;

    public DatabaseLogSink(LogRepository repository)
    {
        _repository = repository;
        _channel = Channel.CreateUnbounded<LogRecord>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _worker = Task.Run(PumpAsync);
    }

    public void Write(LogRecord record) => _channel.Writer.TryWrite(record);

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();

        var finished = await Task.WhenAny(_worker, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
        if (finished != _worker)
        {
            await _shutdown.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Encerramento do aplicativo: nada mais a fazer com o log.
        }

        _shutdown.Dispose();
    }

    private async Task PumpAsync()
    {
        var token = _shutdown.Token;
        var batch = new List<LogRecord>(FlushBatchSize);

        try
        {
            while (await _channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (batch.Count < FlushBatchSize && _channel.Reader.TryRead(out var record))
                {
                    batch.Add(record);
                }

                if (batch.Count == 0)
                {
                    continue;
                }

                try
                {
                    await _repository.AddRangeAsync(batch, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Falha isolada de persistencia: os logs continuam nos arquivos de texto.
                }

                batch.Clear();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
