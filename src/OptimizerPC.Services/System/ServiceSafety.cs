using OptimizerPC.Core;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.System;

/// <summary>
/// Politica de protecao de servicos do Windows. Mecanismos de seguranca nunca podem
/// ser desativados nem parados; servicos essenciais ao funcionamento do sistema e
/// drivers de inicializacao tambem sao bloqueados.
/// </summary>
internal static class ServiceSafety
{
    /// <summary>Servicos de seguranca: bloqueio absoluto, inclusive para parar.</summary>
    private static readonly HashSet<string> SecurityNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WinDefend", "WdNisSvc", "WdNisDrv", "WdBoot", "WdFilter", "WdDevFw", "WdAppController",
        "MsSense", "MsSecFlt", "MsSecCore", "MsSecWfp", "MsSecTm",
        "SecurityHealthService", "SecurityHealthSSO",
        "wscsvc", "mpssvc", "SgrmAgent", "SgrmBroker",
        "webthreatdefsvc", "webthreatdefusersvc", "AppIDSvc", "MsMpEng", "NisSrv"
    };

    /// <summary>Familias de servicos de seguranca identificadas por prefixo.</summary>
    private static readonly string[] SecurityPrefixes = { "WinDefend", "SecurityHealth", "MsSec", "Sgrm", "webthreat", "Defender" };

    /// <summary>Servicos cuja parada ou desativacao compromete o funcionamento do Windows.</summary>
    private static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "Power", "PlugPlay", "BrokerInfrastructure",
        "SystemEventsBroker", "SamSs", "Winmgmt", "CryptSvc", "gpsvc", "ProfSvc", "Themes",
        "nsi", "EventLog", "Schedule", "UserManager", "StateRepository"
    };

    internal static bool IsSecurityCritical(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        if (SecurityNames.Contains(trimmed))
        {
            return true;
        }

        return SecurityPrefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsSystemCritical(string? name) =>
        string.IsNullOrWhiteSpace(name) is false && SystemNames.Contains(name.Trim());

    internal static bool IsProtected(string? name) => IsSecurityCritical(name) || IsSystemCritical(name);

    /// <summary>Motivo pelo qual o modo de inicio nao pode ser alterado, ou vazio quando a alteracao e permitida.</summary>
    internal static string BlockReason(string name, uint serviceType)
    {
        if (IsSecurityCritical(name))
        {
            return "Services.Error.SecurityProtected";
        }

        if (IsSystemCritical(name))
        {
            return "Services.Error.SystemProtected";
        }

        return IsKernelDriver(serviceType) ? "Services.Error.DriverProtected" : string.Empty;
    }

    internal static bool IsKernelDriver(uint serviceType) => (serviceType & 0x0F) != 0;

    internal static string? DescribeBlockReason(WindowsServiceInfo service)
    {
        if (IsSecurityCritical(service.Name))
        {
            return "Services.Keep.Security";
        }

        if (IsSystemCritical(service.Name) || service.IsSystemCritical)
        {
            return "Services.Keep.System";
        }

        return null;
    }
}
