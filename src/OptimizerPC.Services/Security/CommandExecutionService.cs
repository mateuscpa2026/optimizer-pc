using System.Diagnostics;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Security;

namespace OptimizerPC.Services.Security;

/// <summary>
/// Executa somente os comandos nativos previstos na lista branca, sempre a partir de
/// System32, com argumentos autorizados e sem passar por shell de linha de comando.
/// </summary>
public sealed class CommandExecutionService : ICommandExecutionService
{
    private const int MaxCapturedCharacters = 400_000;

    private readonly IAppLogger _logger;
    private readonly IElevationService _elevation;

    public CommandExecutionService(IAppLogger logger, IElevationService elevation)
    {
        _logger = logger;
        _elevation = elevation;
    }

    public async Task<CommandResult> RunAllowedAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        if (CommandAllowList.TryResolve(executablePath, arguments, out var allowed) is false || allowed is null)
        {
            _logger.Warning("Tools", "Execucao recusada: comando fora da lista branca.");
            return new CommandResult(false, -1, string.Empty, string.Empty, "Tools.Error.NotAllowed");
        }

        if (allowed.Elevation == ElevationRequirement.Required && _elevation.IsElevated is false)
        {
            _logger.Info("Tools", allowed.Executable + " exige privilegios administrativos.");
            return new CommandResult(false, -1, string.Empty, string.Empty, "Tools.Error.NeedsElevation");
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process = null;
        try
        {
            _logger.Info("Tools", "Executando " + allowed.Executable + " com argumentos autorizados.");

            process = new Process { StartInfo = startInfo };
            process.Start();

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var output = Clamp(await standardOutput.ConfigureAwait(false));
            var error = Clamp(await standardError.ConfigureAwait(false));

            var messageKey = process.ExitCode == 0 ? "Tools.Result.Completed" : "Tools.Result.Failed";
            _logger.Info("Tools", allowed.Executable + " finalizado com codigo " + process.ExitCode + ".");

            return new CommandResult(true, process.ExitCode, output, error, messageKey);
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            _logger.Info("Tools", allowed.Executable + " cancelado pelo usuario.");
            return new CommandResult(true, -1, string.Empty, string.Empty, "Tools.Result.Cancelled");
        }
        catch (Exception ex)
        {
            KillProcess(process);
            _logger.Error("Tools", "Falha ao executar " + allowed.Executable + ".", ex);
            return new CommandResult(false, -1, string.Empty, ex.Message, "Tools.Result.StartFailed");
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static string Clamp(string value) =>
        value.Length <= MaxCapturedCharacters ? value : value[..MaxCapturedCharacters];

    private static void KillProcess(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // O processo pode ter terminado entre a verificacao e a solicitacao de encerramento.
        }
    }
}
