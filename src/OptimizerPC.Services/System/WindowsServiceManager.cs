using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Interop;
using OptimizerPC.Services.Restore;

namespace OptimizerPC.Services.System;

/// <summary>
/// Leitura e controle de servicos do Windows via Gerenciador de Servicos (SCM).
/// Mecanismos de seguranca, servicos essenciais e drivers de inicializacao nunca sao
/// desativados nem parados. Toda troca de modo de inicio exige elevacao, grava apenas
/// a chave conhecida do servico e registra um ponto de reversao.
/// </summary>
public sealed class WindowsServiceManager : IServiceManager
{
    private const string ServicesRoot = @"SYSTEM\CurrentControlSet\Services\";
    private const string StartValue = "Start";
    private const string DelayedValue = "DelayedAutostart";
    private const string ImagePathValue = "ImagePath";
    private const string TypeValue = "Type";
    private const string DescriptionValue = "Description";

    private const int InitialBufferSize = 64 * 1024;
    private const int StartStopTimeoutMilliseconds = 15000;
    private const int ScmEnumerationTimeoutMs = 5000;

    private readonly IRegistryService _registry;
    private readonly IRestoreService _restore;
    private readonly ILocalizer _localizer;
    private readonly IAppLogger _logger;

    public WindowsServiceManager(IRegistryService registry, IRestoreService restore, ILocalizer localizer, IAppLogger logger)
    {
        _registry = registry;
        _restore = restore;
        _localizer = localizer;
        _logger = logger;
    }

    public Task<IReadOnlyList<WindowsServiceInfo>> GetServicesAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<WindowsServiceInfo>>(() => ReadServices(cancellationToken), cancellationToken);

    public Task<ActionExecutionResult> SetStartModeAsync(WindowsServiceInfo service, ServiceStartMode startMode, CancellationToken cancellationToken = default)
        => Task.Run(() => ApplyStartModeAsync(service, startMode, cancellationToken), cancellationToken);

    public Task<ActionExecutionResult> StartAsync(WindowsServiceInfo service, CancellationToken cancellationToken = default)
        => Task.Run(() => StartServiceInternal(service), cancellationToken);

    public Task<ActionExecutionResult> StopAsync(WindowsServiceInfo service, CancellationToken cancellationToken = default)
        => Task.Run(() => StopServiceInternal(service), cancellationToken);

    private IReadOnlyList<WindowsServiceInfo> ReadServices(CancellationToken cancellationToken)
    {
        // O SCM pode nao responder (servico travado, gerenciador danificado). A enumeracao
        // roda com tempo limite; sem resposta, cai para a listagem do registro, que informa
        // tudo exceto o estado ao vivo, em vez de travar a tela inteira.
        var task = Task.Run(() => EnumerateFromScm(cancellationToken), cancellationToken);

        List<ScmEntry>? enumerated = null;
        try
        {
            if (task.Wait(ScmEnumerationTimeoutMs))
            {
                enumerated = task.Result;
            }
        }
        catch (Exception exception)
        {
            var inner = exception is AggregateException aggregate && aggregate.InnerException is not null
                ? aggregate.InnerException
                : exception;
            _logger.Warning("Services", "Falha ao enumerar os servicos pelo gerenciador.", inner);
        }

        if (enumerated is null)
        {
            _logger.Warning(
                "Services",
                "O gerenciador de servicos nao respondeu a tempo; a lista usa apenas o registro, sem o estado ao vivo.");
            return ReadServicesFromRegistry(cancellationToken);
        }

        var services = new List<WindowsServiceInfo>(enumerated.Count);
        foreach (var entry in enumerated)
        {
            services.Add(Describe(entry.Name, entry.DisplayName, entry.Status));
        }

        return Sort(services);
    }

    private static IReadOnlyList<WindowsServiceInfo> Sort(List<WindowsServiceInfo> services) => services
        .OrderByDescending(s => s.State == WindowsServiceState.Running)
        .ThenBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    /// <summary>
    /// Lista de reserva lida somente do registro: nomes, modo de inicio, caminho e descricao.
    /// O estado ao vivo fica como desconhecido, porque so o SCM o informa.
    /// </summary>
    private IReadOnlyList<WindowsServiceInfo> ReadServicesFromRegistry(CancellationToken cancellationToken)
    {
        var services = new List<WindowsServiceInfo>();

        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(ServicesRoot);
            if (root is null)
            {
                return services;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var subKey = ServicesRoot + name;
                var serviceType = ReadInt(subKey, TypeValue) ?? 0;

                // Somente servicos Win32 (bits 0x10/0x20); drivers de kernel ficam de fora,
                // como na enumeracao pelo SCM.
                if ((serviceType & (int)NativeServices.SERVICE_WIN32) == 0)
                {
                    continue;
                }

                var start = ReadInt(subKey, StartValue);
                var delayed = ReadInt(subKey, DelayedValue);
                var imagePath = ReadString(subKey, ImagePathValue);
                var isDriver = ServiceSafety.IsKernelDriver((uint)serviceType);

                services.Add(new WindowsServiceInfo
                {
                    Name = name,
                    DisplayName = name,
                    Description = ReadDescription(subKey),
                    State = WindowsServiceState.Unknown,
                    StartMode = MapStartMode(start, delayed),
                    ProcessId = 0,
                    ExecutablePath = ExpandPath(imagePath),
                    IsSystemCritical = ServiceSafety.IsProtected(name) || isDriver,
                    CanStop = false,
                    IsMicrosoft = IsWindowsComponent(imagePath)
                });
            }
        }
        catch (Exception exception)
        {
            _logger.Warning("Services", "Falha ao listar os servicos pelo registro.", exception);
        }

        return Sort(services);
    }

    private sealed record ScmEntry(string Name, string DisplayName, SERVICE_STATUS_PROCESS Status);

    /// <summary>Devolve nulo quando o SCM nao abre ou a enumeracao falha.</summary>
    private List<ScmEntry>? EnumerateFromScm(CancellationToken cancellationToken)
    {
        var entries = new List<ScmEntry>();

        using var manager = NativeServices.OpenSCManager(null, null, NativeServices.SC_MANAGER_CONNECT | NativeServices.SC_MANAGER_ENUMERATE_SERVICE);
        if (manager.IsInvalid)
        {
            _logger.Warning("Services", "Nao foi possivel abrir o gerenciador de servicos.", null);
            return null;
        }

        var bufferSize = InitialBufferSize;
        var buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            var entrySize = Marshal.SizeOf<NativeServices.ENUM_SERVICE_STATUS_PROCESS>();
            uint resumeHandle = 0;

            while (cancellationToken.IsCancellationRequested is false)
            {
                var enumerated = NativeServices.EnumServicesStatusEx(
                    manager,
                    0,
                    NativeServices.SERVICE_WIN32,
                    NativeServices.SERVICE_STATE_ALL,
                    buffer,
                    (uint)bufferSize,
                    out var bytesNeeded,
                    out var servicesReturned,
                    ref resumeHandle,
                    null);

                if (enumerated is false)
                {
                    // A lista cresceu entre as chamadas: o buffer e ampliado e a leitura recomeca.
                    if (bytesNeeded > bufferSize)
                    {
                        buffer = Reallocate(buffer, (int)bytesNeeded);
                        bufferSize = (int)bytesNeeded;
                        continue;
                    }

                    break;
                }

                if (servicesReturned == 0)
                {
                    break;
                }

                for (var index = 0; index < servicesReturned; index++)
                {
                    var pointer = IntPtr.Add(buffer, index * entrySize);
                    var entry = Marshal.PtrToStructure<NativeServices.ENUM_SERVICE_STATUS_PROCESS>(pointer);
                    var name = entry.ServiceName;

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    entries.Add(new ScmEntry(name, entry.DisplayName ?? name, entry.ServiceStatusProcess));
                }
            }
        }
        catch (Exception exception)
        {
            _logger.Warning("Services", "Falha ao enumerar os servicos do Windows.", exception);
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return entries;
    }

    private WindowsServiceInfo Describe(string name, string displayName, SERVICE_STATUS_PROCESS status)
    {
        var subKey = ServicesRoot + name;
        var start = ReadInt(subKey, StartValue);
        var delayed = ReadInt(subKey, DelayedValue);
        var serviceType = ReadInt(subKey, TypeValue) ?? (int)NativeServices.SERVICE_WIN32;
        var imagePath = ReadString(subKey, ImagePathValue);

        var startMode = MapStartMode(start, delayed);
        var state = MapState(status.CurrentState);
        var isDriver = ServiceSafety.IsKernelDriver((uint)serviceType);
        var canStop = state == WindowsServiceState.Running && (status.ControlsAccepted & NativeServices.SERVICE_ACCEPT_STOP) != 0;

        return new WindowsServiceInfo
        {
            Name = name,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName,
            Description = ReadDescription(subKey),
            State = state,
            StartMode = startMode,
            ProcessId = (int)status.ProcessId,
            ExecutablePath = ExpandPath(imagePath),
            IsSystemCritical = ServiceSafety.IsProtected(name) || isDriver,
            CanStop = canStop && ServiceSafety.IsProtected(name) is false,
            IsMicrosoft = IsWindowsComponent(imagePath)
        };
    }

    private async Task<ActionExecutionResult> ApplyStartModeAsync(WindowsServiceInfo service, ServiceStartMode startMode, CancellationToken cancellationToken)
    {
        const OptimizationActionKind kind = OptimizationActionKind.SetServiceStartMode;
        var blockReason = ServiceSafety.BlockReason(service.Name, ReadServiceType(service.Name));

        if (blockReason.Length > 0)
        {
            return Blocked(service, kind, blockReason);
        }

        if (service.StartMode == startMode)
        {
            return Blocked(service, kind, "Services.Result.AlreadySet");
        }

        if (NativeProcess.IsCurrentProcessElevated() is false)
        {
            return Blocked(service, kind, "Services.Error.NeedsElevation");
        }

        var subKey = ServicesRoot + service.Name;
        var previousStart = ReadInt(subKey, StartValue);
        var previousDelayed = ReadInt(subKey, DelayedValue);

        using var manager = NativeServices.OpenSCManager(null, null, NativeServices.SC_MANAGER_CONNECT);
        if (manager.IsInvalid)
        {
            return Blocked(service, kind, "Services.Error.OpenFailed");
        }

        using var handle = NativeServices.OpenService(manager, service.Name, NativeServices.SERVICE_CHANGE_CONFIG | NativeServices.SERVICE_QUERY_CONFIG);
        if (handle.IsInvalid)
        {
            _logger.Warning("Services", "Sem permissao para alterar o servico " + service.Name + ".", null);
            return Blocked(service, kind, "Services.Error.WriteFailed");
        }

        var applied = NativeServices.ChangeServiceConfig(
            handle,
            NativeServices.SERVICE_NO_CHANGE,
            MapStartType(startMode),
            NativeServices.SERVICE_NO_CHANGE,
            null,
            null,
            IntPtr.Zero,
            null,
            null,
            null,
            null);

        if (applied is false)
        {
            _logger.Warning("Services", "Nao foi possivel alterar o modo de inicio de " + service.Name + ".", null);
            return Blocked(service, kind, "Services.Error.WriteFailed");
        }

        if (startMode is ServiceStartMode.Automatic or ServiceStartMode.AutomaticDelayed)
        {
            TrySetDelayedStart(handle, startMode == ServiceStartMode.AutomaticDelayed);
        }

        var payload = RestorePayload.ForServiceStartMode("LocalMachine", subKey, previousStart, previousDelayed);
        var recordId = await _restore.RegisterAsync(
            new RestoreRecord
            {
                Kind = RestoreRecordKind.RegistryValue,
                TitleKey = "Restore.Kind.ServiceStartMode",
                Description = service.DisplayName,
                SourceAction = nameof(OptimizationActionKind.SetServiceStartMode),
                TargetPath = subKey,
                PayloadJson = payload
            },
            cancellationToken).ConfigureAwait(false);

        service.StartMode = startMode;
        _logger.Info("Services", "Modo de inicio de " + service.Name + " alterado para " + startMode + ".");

        return new ActionExecutionResult
        {
            ActionId = service.Name,
            Kind = kind,
            TitleKey = "Services.Action.SetStartMode",
            Success = true,
            MessageKey = "Services.Result.StartModeChanged",
            Detail = service.DisplayName + " · " + _localizer["Services.StartMode." + service.StartMode],
            RestoreRecordId = recordId
        };
    }

    private ActionExecutionResult StartServiceInternal(WindowsServiceInfo service)
    {
        const OptimizationActionKind kind = OptimizationActionKind.StartWindowsService;

        if (ServiceSafety.IsSecurityCritical(service.Name))
        {
            return Blocked(service, kind, "Services.Error.SecurityProtected");
        }

        if (service.State == WindowsServiceState.Running)
        {
            return Blocked(service, kind, "Services.Result.AlreadyRunning");
        }

        if (NativeProcess.IsCurrentProcessElevated() is false)
        {
            return Blocked(service, kind, "Services.Error.NeedsElevation");
        }

        using var manager = NativeServices.OpenSCManager(null, null, NativeServices.SC_MANAGER_CONNECT);
        if (manager.IsInvalid)
        {
            return Blocked(service, kind, "Services.Error.OpenFailed");
        }

        using var handle = NativeServices.OpenService(manager, service.Name, NativeServices.SERVICE_START | NativeServices.SERVICE_QUERY_STATUS);
        if (handle.IsInvalid)
        {
            return Blocked(service, kind, "Services.Error.OpenFailed");
        }

        if (NativeServices.StartService(handle, 0, null) is false)
        {
            _logger.Warning("Services", "Nao foi possivel iniciar o servico " + service.Name + ".", null);
            return Blocked(service, kind, "Services.Error.StartFailed");
        }

        var running = WaitForState(handle, NativeServices.SERVICE_RUNNING);
        service.State = running ? WindowsServiceState.Running : WindowsServiceState.StartPending;
        service.CanStop = running;

        _logger.Info("Services", "Servico iniciado: " + service.Name + ".");

        return new ActionExecutionResult
        {
            ActionId = service.Name,
            Kind = kind,
            TitleKey = "Services.Action.Start",
            Success = true,
            MessageKey = running ? "Services.Result.Started" : "Services.Result.StartRequested",
            Detail = service.DisplayName
        };
    }

    private ActionExecutionResult StopServiceInternal(WindowsServiceInfo service)
    {
        const OptimizationActionKind kind = OptimizationActionKind.StopWindowsService;

        if (ServiceSafety.IsSecurityCritical(service.Name))
        {
            return Blocked(service, kind, "Services.Error.SecurityProtected");
        }

        if (ServiceSafety.IsSystemCritical(service.Name) || service.IsSystemCritical)
        {
            return Blocked(service, kind, "Services.Error.SystemProtected");
        }

        if (service.State != WindowsServiceState.Running)
        {
            return Blocked(service, kind, "Services.Result.AlreadyStopped");
        }

        if (service.CanStop is false)
        {
            return Blocked(service, kind, "Services.Error.CannotStop");
        }

        if (NativeProcess.IsCurrentProcessElevated() is false)
        {
            return Blocked(service, kind, "Services.Error.NeedsElevation");
        }

        using var manager = NativeServices.OpenSCManager(null, null, NativeServices.SC_MANAGER_CONNECT);
        if (manager.IsInvalid)
        {
            return Blocked(service, kind, "Services.Error.OpenFailed");
        }

        using var handle = NativeServices.OpenService(manager, service.Name, NativeServices.SERVICE_STOP | NativeServices.SERVICE_QUERY_STATUS);
        if (handle.IsInvalid)
        {
            return Blocked(service, kind, "Services.Error.OpenFailed");
        }

        var statusBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<SERVICE_STATUS_PROCESS>());
        try
        {
            if (NativeServices.ControlService(handle, NativeServices.SERVICE_CONTROL_STOP, statusBuffer) is false)
            {
                _logger.Warning("Services", "Nao foi possivel parar o servico " + service.Name + ".", null);
                return Blocked(service, kind, "Services.Error.StopFailed");
            }

            var stopped = WaitForState(handle, NativeServices.SERVICE_STOPPED);
            service.State = stopped ? WindowsServiceState.Stopped : WindowsServiceState.StopPending;
            service.CanStop = false;

            _logger.Info("Services", "Servico parado: " + service.Name + ".");

            return new ActionExecutionResult
            {
                ActionId = service.Name,
                Kind = kind,
                TitleKey = "Services.Action.Stop",
                Success = true,
                MessageKey = stopped ? "Services.Result.Stopped" : "Services.Result.StopRequested",
                Detail = service.DisplayName
            };
        }
        finally
        {
            Marshal.FreeHGlobal(statusBuffer);
        }
    }

    private static ActionExecutionResult Blocked(WindowsServiceInfo service, OptimizationActionKind kind, string messageKey) => new()
    {
        ActionId = service.Name,
        Kind = kind,
        TitleKey = "Services.Action.Toggle",
        Success = false,
        Skipped = true,
        MessageKey = messageKey,
        Detail = service.DisplayName
    };

    private static bool WaitForState(SafeWaitHandle handle, uint desiredState)
    {
        var size = Marshal.SizeOf<SERVICE_STATUS_PROCESS>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            var deadline = Environment.TickCount64 + StartStopTimeoutMilliseconds;

            while (true)
            {
                if (NativeServices.QueryServiceStatusEx(handle, (int)NativeServices.SC_STATUS_PROCESS_INFO, buffer, (uint)size, out _) is false)
                {
                    return false;
                }

                var status = Marshal.PtrToStructure<SERVICE_STATUS_PROCESS>(buffer);
                if (status.CurrentState == desiredState)
                {
                    return true;
                }

                if (Environment.TickCount64 >= deadline)
                {
                    return false;
                }

                Thread.Sleep(250);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void TrySetDelayedStart(SafeWaitHandle handle, bool delayed)
    {
        var info = new NativeServices.SERVICE_DELAYED_AUTO_START_INFO { IsDelayedAutoStart = delayed };
        var size = Marshal.SizeOf<NativeServices.SERVICE_DELAYED_AUTO_START_INFO>();
        var pointer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            NativeServices.ChangeServiceConfig2(handle, NativeServices.SERVICE_CONFIG_DELAYED_AUTO_START_INFO, pointer);
        }
        catch (Exception)
        {
            // A marcacao de inicio atrasado e complementar: a troca principal ja foi aplicada.
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    /// <summary>
    /// A descricao e lida direto do registro (a mesma origem que o SCM consulta). Abrir o
    /// servico no SCM para cada entrada seria uma chamada RPC por servico: alem de lenta,
    /// um unico servico que nao responde travaria a enumeracao inteira.
    /// </summary>
    private string ReadDescription(string subKey)
    {
        try
        {
            return ReadString(subKey, DescriptionValue)?.Trim() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private uint ReadServiceType(string name) =>
        (uint)(ReadInt(ServicesRoot + name, TypeValue) ?? (int)NativeServices.SERVICE_WIN32);

    private int? ReadInt(string subKey, string valueName) =>
        _registry.GetValue(RegistryHiveKind.LocalMachine, subKey, valueName)?.IntValue;

    private string? ReadString(string subKey, string valueName) =>
        _registry.GetValue(RegistryHiveKind.LocalMachine, subKey, valueName)?.StringValue;

    internal static ServiceStartMode MapStartMode(int? start, int? delayed) => start switch
    {
        0 => ServiceStartMode.Boot,
        1 => ServiceStartMode.System,
        2 => delayed == 1 ? ServiceStartMode.AutomaticDelayed : ServiceStartMode.Automatic,
        3 => ServiceStartMode.Manual,
        4 => ServiceStartMode.Disabled,
        _ => ServiceStartMode.Unknown
    };

    internal static uint MapStartType(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Boot => NativeServices.SERVICE_BOOT_START,
        ServiceStartMode.System => NativeServices.SERVICE_SYSTEM_START,
        ServiceStartMode.Automatic => NativeServices.SERVICE_AUTO_START,
        ServiceStartMode.AutomaticDelayed => NativeServices.SERVICE_AUTO_START,
        ServiceStartMode.Manual => NativeServices.SERVICE_DEMAND_START,
        ServiceStartMode.Disabled => NativeServices.SERVICE_DISABLED,
        _ => NativeServices.SERVICE_NO_CHANGE
    };

    internal static WindowsServiceState MapState(uint currentState) => currentState switch
    {
        1 => WindowsServiceState.Stopped,
        2 => WindowsServiceState.StartPending,
        3 => WindowsServiceState.StopPending,
        4 => WindowsServiceState.Running,
        5 => WindowsServiceState.ContinuePending,
        6 => WindowsServiceState.PausePending,
        7 => WindowsServiceState.Paused,
        _ => WindowsServiceState.Unknown
    };

    private static string ExpandPath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return string.Empty;
        }

        try
        {
            return Environment.ExpandEnvironmentVariables(imagePath.Trim());
        }
        catch (Exception)
        {
            return imagePath.Trim();
        }
    }

    private static bool IsWindowsComponent(string? imagePath)
    {
        var expanded = ExpandPath(imagePath);
        if (expanded.Length == 0)
        {
            return false;
        }

        if (expanded.Contains("svchost.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return windows.Length > 0 && expanded.StartsWith(windows, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IntPtr Reallocate(IntPtr buffer, int size)
    {
        Marshal.FreeHGlobal(buffer);
        return Marshal.AllocHGlobal(size);
    }
}
