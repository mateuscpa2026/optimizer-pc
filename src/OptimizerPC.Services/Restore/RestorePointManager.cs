using System.Runtime.InteropServices;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Interop;

namespace OptimizerPC.Services.Restore;

/// <summary>
/// Pontos de restauracao do Windows. A criacao e apenas solicitada a API oficial
/// (srclient): quem decide criar, quando criar e quanto tempo manter e o proprio
/// Windows. Nenhuma configuracao de protecao e alterada por baixo dos panos; quando
/// ela precisa ser ligada, o painel oficial do Windows e aberto para o usuario.
/// </summary>
public sealed class RestorePointManager : IRestorePointManager
{
    private const string LogCategory = "RestorePoint";

    private const string SystemRestoreKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
    private const string SystemRestorePolicyKey = @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore";
    private const string DisableValue = "DisableSR";

    private const string ProtectionPanelExecutable = "SystemPropertiesProtection.exe";
    private const string RestoreWizardExecutable = "rstrui.exe";

    private readonly IRegistryService _registry;
    private readonly ILocalizer _localizer;
    private readonly IAppLogger _logger;

    private readonly object _gate = new();
    private readonly List<RestorePointInfo> _created = new();

    public RestorePointManager(IRegistryService registry, ILocalizer localizer, IAppLogger logger)
    {
        _registry = registry;
        _localizer = localizer;
        _logger = logger;
    }

    public Task<SystemRestoreStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.Run(ReadStatus, cancellationToken);

    public Task<IReadOnlyList<RestorePointInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        // O Windows nao expoe a lista de pontos sem WMI (indisponivel neste aplicativo).
        // A lista mostra o que o proprio Optimizer PC solicitou nesta sessao; para ver
        // todos os pontos, a interface abre a Restauracao do Sistema.
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<RestorePointInfo>>(_created.ToArray());
        }
    }

    public Task<RestorePointCreationResult> CreateAsync(string description, CancellationToken cancellationToken = default)
        => Task.Run(() => Create(description), cancellationToken);

    public async Task<RestorePointCreationResult> EnableProtectionAsync(VolumeInfo systemVolume, CancellationToken cancellationToken = default)
    {
        var opened = await OpenSystemProtectionAsync(cancellationToken).ConfigureAwait(false);

        return new RestorePointCreationResult
        {
            Success = opened,
            MessageKey = opened ? "Restore.Point.EnableManual" : "Restore.Point.OpenFailed",
            RequiresElevation = true
        };
    }

    public Task<bool> OpenSystemProtectionAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => OpenTool(ProtectionPanelExecutable, "a Protecao do Sistema"), cancellationToken);

    public Task<bool> OpenSystemRestoreAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => OpenTool(RestoreWizardExecutable, "a Restauracao do Sistema"), cancellationToken);

    private SystemRestoreStatus ReadStatus()
    {
        var policyDisabled = ReadFlag(SystemRestorePolicyKey, DisableValue) == 1;
        var locallyDisabled = ReadFlag(SystemRestoreKey, DisableValue) == 1;

        return new SystemRestoreStatus
        {
            IsSupported = true,
            IsEnabled = policyDisabled is false && locallyDisabled is false,
            Message = policyDisabled
                ? _localizer["Restore.Status.PolicyDisabled"]
                : locallyDisabled
                    ? _localizer["Restore.Status.Disabled"]
                    : _localizer["Restore.Status.Enabled"],
            Points = Snapshot()
        };
    }

    private RestorePointCreationResult Create(string description)
    {
        if (NativeProcess.IsCurrentProcessElevated() is false)
        {
            return new RestorePointCreationResult
            {
                Success = false,
                MessageKey = "Restore.Point.NeedsElevation",
                RequiresElevation = true
            };
        }

        var text = (description ?? string.Empty).Trim();
        if (text.Length is 0)
        {
            text = "Optimizer PC";
        }

        if (text.Length > NativeRestorePoint.MaxDescriptionLength - 1)
        {
            text = text[..(NativeRestorePoint.MaxDescriptionLength - 1)];
        }

        var info = new NativeRestorePoint.RESTOREPOINTINFO
        {
            EventType = NativeRestorePoint.BeginSystemChange,
            RestorePointType = NativeRestorePoint.ModifySettings,
            SequenceNumber = 0,
            Description = text
        };

        try
        {
            if (NativeRestorePoint.SRSetRestorePoint(ref info, out var status) is false)
            {
                // O Windows recusa a criacao, por exemplo, quando a protecao esta
                // desligada ou quando um ponto recente ja existe: nada e inventado aqui.
                _logger.Warning(
                    LogCategory,
                    "O Windows recusou a criacao do ponto de restauracao (codigo " + status.Status + ").",
                    null);

                return new RestorePointCreationResult
                {
                    Success = false,
                    MessageKey = "Restore.Point.Refused"
                };
            }

            var sequence = (int)status.SequenceNumber;
            Remember(new RestorePointInfo
            {
                SequenceNumber = sequence,
                Description = text,
                CreationTimeUtc = DateTime.UtcNow,
                Type = _localizer["Restore.Point.TypeApplication"],
                IsSystemRestorePoint = false
            });

            _logger.Info(LogCategory, "Ponto de restauracao solicitado ao Windows (sequencia " + sequence + ").");

            return new RestorePointCreationResult
            {
                Success = true,
                MessageKey = "Restore.Point.Created",
                SequenceNumber = sequence
            };
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory, "Falha ao solicitar o ponto de restauracao.", ex);

            return new RestorePointCreationResult
            {
                Success = false,
                MessageKey = "Restore.Point.Failed"
            };
        }
    }

    private bool OpenTool(string executable, string toolName)
    {
        var path = Path.Combine(Environment.SystemDirectory, executable);
        if (File.Exists(path) is false)
        {
            _logger.Warning(LogCategory, "Ferramenta oficial do Windows nao encontrada: " + executable + ".", null);
            return false;
        }

        // As duas ferramentas oficiais exigem elevacao: o Windows exibe o proprio aviso.
        if (NativeShell.TryStart(path, null, elevate: true, hidden: false, out var error))
        {
            _logger.Info(LogCategory, "Ferramenta oficial do Windows aberta: " + executable + ".");
            return true;
        }

        _logger.Warning(LogCategory, "Nao foi possivel abrir " + toolName + ": " + error, null);
        return false;
    }

    private void Remember(RestorePointInfo point)
    {
        lock (_gate)
        {
            _created.Insert(0, point);
        }
    }

    private IReadOnlyList<RestorePointInfo> Snapshot()
    {
        lock (_gate)
        {
            return _created.ToArray();
        }
    }

    private int? ReadFlag(string subKey, string valueName)
    {
        try
        {
            var value = _registry.GetValue(RegistryHiveKind.LocalMachine, subKey, valueName);
            if (value is null)
            {
                return null;
            }

            if (value.IntValue.HasValue)
            {
                return value.IntValue;
            }

            return int.TryParse(value.StringValue, out var parsed) ? parsed : null;
        }
        catch (Exception ex)
        {
            // Politica inacessivel nao significa desligada: o estado fica indefinido.
            _logger.Warning(LogCategory, "Nao foi possivel ler " + subKey + "\\" + valueName + ".", ex);
            return null;
        }
    }
}
