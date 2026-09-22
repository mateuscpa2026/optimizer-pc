using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.System;

/// <summary>
/// Regras de protecao de processos. O encerramento nunca e permitido para processos
/// essenciais do Windows nem para o proprio Optimizer PC.
/// </summary>
internal static class ProcessSafety
{
    private static readonly HashSet<string> CriticalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system",
        "idle",
        "registry",
        "memory compression",
        "secure system",
        "smss",
        "csrss",
        "wininit",
        "winlogon",
        "services",
        "lsass",
        "lsaiso",
        "fontdrvhost",
        "svchost",
        "dwm",
        "audiodg",
        "sihost",
        "ctfmon",
        "explorer",
        "wudfhost",
        "taskhostw",
        "spoolsv",
        "searchindexer",
        "msmpeng",
        "nissrv",
        "securityhealthservice",
        "securityhealthsystray",
        "sppsvc",
        "wlanext",
        "wslservice"
    };

    private static readonly HashSet<string> ProtectedOwnProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "OptimizerPC"
    };

    internal static bool IsCritical(ProcessInfoModel process) => IsCritical(process.Name, process.Id);

    internal static bool IsCritical(string? name, int id)
    {
        var normalized = NormalizeName(name);

        if (CriticalNames.Contains(normalized) || ProtectedOwnProcessNames.Contains(normalized))
        {
            return true;
        }

        if (id <= 4)
        {
            return true;
        }

        return id == Environment.ProcessId;
    }

    /// <summary>
    /// Motivo pelo qual o processo nao pode ser encerrado, ou uma cadeia vazia quando a acao e permitida.
    /// </summary>
    internal static string DescribeBlockReason(ProcessInfoModel process)
    {
        if (process.Id == Environment.ProcessId)
        {
            return "Process.Keep.Block.Self";
        }

        if (NormalizeName(process.Name) is "msmpeng" or "nissrv" or "securityhealthservice" or "securityhealthsystray")
        {
            return "Process.Keep.Block.Security";
        }

        return IsCritical(process) ? "Process.Keep.Block.Critical" : string.Empty;
    }

    private static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }
}
