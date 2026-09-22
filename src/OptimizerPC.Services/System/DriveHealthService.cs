using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Core.Security;

namespace OptimizerPC.Services.System;

/// <summary>
/// Saude dos discos e manutencao de volumes. As operacoes de manutencao usam apenas
/// comandos nativos autorizados (defrag com otimizacao e chkdsk em modo somente leitura)
/// e exigem elevacao. Nenhuma correcao, formatacao ou alteracao de firmware e executada.
/// </summary>
public sealed class DriveHealthService : IDriveHealthService
{
    private const string TrimToolId = "defrag";
    private const string ChkdskToolId = "chkdsk";

    private readonly IElevationService _elevation;
    private readonly ICommandExecutionService _commands;
    private readonly IAppLogger _logger;

    public DriveHealthService(IElevationService elevation, ICommandExecutionService commands, IAppLogger logger)
    {
        _elevation = elevation;
        _commands = commands;
        _logger = logger;
    }

    public Task<IReadOnlyList<DeviceHealthReport>> GetHealthReportsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<DeviceHealthReport>>(() =>
        {
            var reports = DriveHealthReader.ReadAll()
                .Select(ToReport)
                .ToArray();

            return reports;
        }, cancellationToken);

    /// <summary>
    /// Otimizacao do volume. Em unidades de estado solido o Windows executa o TRIM;
    /// em discos mecanicos executa a desfragmentacao. A operacao e sempre executada
    /// pelo proprio Windows, sem parametros agressivos.
    /// </summary>
    public Task<VolumeMaintenanceResult> RunTrimAsync(VolumeInfo volume, CancellationToken cancellationToken = default) =>
        RunVolumeCommandAsync(volume, TrimToolId, "/o", "Volume.Result.Trimmed", cancellationToken);

    /// <summary>
    /// Verificacao de integridade somente leitura. Nenhuma correcao e aplicada:
    /// o comando e executado exclusivamente com o parametro de varredura.
    /// </summary>
    public Task<VolumeMaintenanceResult> RunChkdskScanAsync(VolumeInfo volume, CancellationToken cancellationToken = default) =>
        RunVolumeCommandAsync(volume, ChkdskToolId, "/scan", "Volume.Result.Checked", cancellationToken);

    private async Task<VolumeMaintenanceResult> RunVolumeCommandAsync(
        VolumeInfo volume,
        string toolId,
        string argument,
        string successKey,
        CancellationToken cancellationToken)
    {
        if (volume.IsReady is false)
        {
            return Failure("Volume.Error.NotReady");
        }

        if (_elevation.IsElevated is false)
        {
            return new VolumeMaintenanceResult
            {
                Started = false,
                MessageKey = "Volume.Error.NeedsElevation",
                RequiresElevation = true
            };
        }

        var command = CommandAllowList.All.FirstOrDefault(c =>
            string.Equals(Path.GetFileNameWithoutExtension(c.Executable), toolId, StringComparison.OrdinalIgnoreCase));

        if (command is null)
        {
            return Failure("Volume.Error.NotAvailable");
        }

        var executable = CommandAllowList.ResolveExecutablePath(command);
        var arguments = new[] { volume.DriveLetter + ":", argument };

        if (CommandAllowList.TryResolve(executable, arguments, out _) is false)
        {
            return Failure("Volume.Error.NotAvailable");
        }

        _logger.Info("Storage", "Manutencao de volume solicitada: " + command.Executable + " " + string.Join(' ', arguments) + ".");

        var result = await _commands.RunAllowedAsync(executable, arguments, cancellationToken).ConfigureAwait(false);

        if (result.Started is false)
        {
            return new VolumeMaintenanceResult
            {
                Started = false,
                ExitCode = result.ExitCode,
                Output = result.StandardError,
                MessageKey = result.MessageKey
            };
        }

        return new VolumeMaintenanceResult
        {
            Started = true,
            ExitCode = result.ExitCode,
            Output = result.StandardOutput,
            MessageKey = result.ExitCode == 0 ? successKey : "Volume.Result.Failed"
        };
    }

    private static VolumeMaintenanceResult Failure(string messageKey) => new()
    {
        Started = false,
        MessageKey = messageKey
    };

    private static DeviceHealthReport ToReport(DiskHealth health) => new()
    {
        DeviceName = "Disco " + health.Disk.Index,
        Model = string.IsNullOrWhiteSpace(health.Disk.Model) ? null : health.Disk.Model,
        MediaType = health.MediaType,
        BusType = (StorageBusType)health.Disk.BusType,
        Status = health.Status,
        TemperatureCelsius = health.TemperatureCelsius,
        PowerOnHours = health.PowerOnHours,
        TotalBytesWritten = health.TotalBytesWritten,
        WearPercent = health.WearPercent,
        SmartAvailable = health.SmartAvailable,
        SourceKey = health.SourceKey,
        Notes = health.Notes
    };
}
