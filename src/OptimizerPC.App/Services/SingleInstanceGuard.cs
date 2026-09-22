using System.Threading;

namespace OptimizerPC.App.Services;

/// <summary>
/// Garante uma unica instancia visivel do aplicativo. A tarefa de manutencao
/// agendada roda com --maintenance e nao disputa a instancia da interface.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\OptimizerPC.SingleInstance";
    private const string ActivationEventName = @"Local\OptimizerPC.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listener;
    private bool _ownsMutex;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        _ownsMutex = created;
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
    }

    public bool IsPrimaryInstance => _ownsMutex;

    /// <summary>
    /// Pede a instancia existente que traga a janela para a frente.
    /// </summary>
    public void SignalExistingInstance()
    {
        try
        {
            _activation.Set();
        }
        catch (Exception)
        {
            // Sem sinal: a outra instancia apenas nao sera trazida para a frente.
        }
    }

    /// <summary>
    /// Escuta pedidos de ativacao vindos de instancias secundarias.
    /// </summary>
    public void ListenToActivationRequests(Action onActivated)
    {
        if (_ownsMutex is false)
        {
            return;
        }

        var token = _cancellation.Token;
        _listener = Task.Run(() =>
        {
            var handles = new WaitHandle[] { _activation, token.WaitHandle };
            while (token.IsCancellationRequested is false)
            {
                var index = WaitHandle.WaitAny(handles);
                if (index != 0)
                {
                    return;
                }

                try
                {
                    onActivated();
                }
                catch (Exception)
                {
                    // A janela pode estar fechando; ignorar.
                }
            }
        });
    }

    public void Dispose()
    {
        _cancellation.Cancel();

        try
        {
            _activation.Set();
            _listener?.Wait(TimeSpan.FromMilliseconds(400));
        }
        catch (Exception)
        {
            // Encerramento.
        }

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Nao era o dono no momento do encerramento.
            }
        }

        _activation.Dispose();
        _mutex.Dispose();
        _cancellation.Dispose();
    }
}
