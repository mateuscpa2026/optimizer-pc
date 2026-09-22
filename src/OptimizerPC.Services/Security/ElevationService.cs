using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Security;
using OptimizerPC.Services.Interop;

namespace OptimizerPC.Services.Security;

/// <summary>
/// Elevacao pelo mecanismo oficial do Windows (UAC). O aplicativo continua funcionando
/// sem privilegios; a elevacao e solicitada apenas para as acoes que realmente precisam.
/// </summary>
public sealed class ElevationService : IElevationService
{
    private readonly IAppLogger _logger;

    public ElevationService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsElevated => NativeProcess.IsCurrentProcessElevated();

    public Task<bool> RestartElevatedAsync(string reasonKey)
    {
        return Task.Run(() =>
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                _logger.Warning("Elevation", "Nao foi possivel identificar o executavel atual para reiniciar.");
                return false;
            }

            // A nova instancia recebe o aviso de que substitui a atual (encerrada logo em seguida).
            var started = NativeShell.TryStart(executable, ElevatedRestartArgument, elevate: true, hidden: false, out var error);
            if (started is false)
            {
                _logger.Warning("Elevation", "Elevacao nao concedida pelo Windows. Motivo informado: " + reasonKey);
                return false;
            }

            _logger.Info("Elevation", "Nova instancia elevada iniciada. Motivo: " + reasonKey);
            return true;
        });
    }

    public Task<bool> RunElevatedCommandAsync(string executablePath, IReadOnlyList<string> arguments, string workingDirectory)
    {
        return Task.Run(() =>
        {
            if (CommandAllowList.TryResolve(executablePath, arguments, out var allowed) is false || allowed is null)
            {
                _logger.Warning("Elevation", "Comando recusado pela lista branca antes da elevacao.");
                return false;
            }

            var commandLine = string.Join(' ', arguments.Select(QuoteArgument));
            var started = NativeShell.TryStart(executablePath, commandLine, elevate: true, hidden: false, workingDirectory, out var error);
            if (started is false)
            {
                _logger.Warning("Elevation", "Execucao elevada de " + allowed.Executable + " nao autorizada: " + error);
                return false;
            }

            _logger.Info("Elevation", allowed.Executable + " iniciado em janela elevada a pedido do usuario.");
            return true;
        });
    }

    /// <summary>Argumento usado pela instancia elevada para aguardar o encerramento da anterior.</summary>
    public const string ElevatedRestartArgument = "--elevated-restart";

    private static string QuoteArgument(string argument) =>
        argument.Contains(' ') ? "\"" + argument + "\"" : argument;
}
