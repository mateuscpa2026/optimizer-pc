using System.Text.RegularExpressions;

namespace OptimizerPC.Core.Security;

/// <summary>Comando nativo do Windows autorizado pelo aplicativo.</summary>
public sealed record AllowedCommand(
    string Executable,
    string DescriptionKey,
    IReadOnlyList<string> AllowedArguments,
    bool AllowDriveLetterArgument,
    ElevationRequirement Elevation,
    bool IsReadOnly,
    IReadOnlyList<ArgumentValueRule>? ValueRules = null);

/// <summary>
/// Valor aceito por uma opcao que recebe argumento (ex.: /tn NomeDaTarefa). A regra e
/// declarada junto da lista branca: sem ela, a opcao sozinha nao autoriza valor algum.
/// O marcador %SELF% representa o proprio executavel do aplicativo.
/// </summary>
public sealed record ArgumentValueRule(string Option, string Pattern);

/// <summary>
/// Lista branca de comandos nativos. O aplicativo nunca executa comandos arbitrarios:
/// apenas os executaveis abaixo, a partir de %SystemRoot%\System32, com argumentos
/// previamente autorizados e sem passagem por shell.
/// </summary>
public static class CommandAllowList
{
    public static IReadOnlyList<AllowedCommand> All { get; } = new[]
    {
        new AllowedCommand(
            "sfc.exe",
            "Tools.Sfc.Description",
            new[] { "/scannow", "/verifyonly", "/scanfile" },
            false,
            ElevationRequirement.Required,
            false),

        new AllowedCommand(
            "dism.exe",
            "Tools.Dism.Description",
            new[] { "/online", "/cleanup-image", "/scanhealth", "/checkhealth", "/restorehealth" },
            false,
            ElevationRequirement.Required,
            false),

        new AllowedCommand(
            "chkdsk.exe",
            "Tools.Chkdsk.Description",
            new[] { "/scan", "/perf" },
            true,
            ElevationRequirement.Required,
            true),

        new AllowedCommand(
            "powercfg.exe",
            "Tools.PowerCfg.Description",
            new[] { "/list", "/getactivescheme", "/setactive", "/energy", "/lastwake" },
            false,
            ElevationRequirement.None,
            false),

        new AllowedCommand(
            "defrag.exe",
            "Tools.Defrag.Description",
            new[] { "/o", "/c", "/a", "/v" },
            true,
            ElevationRequirement.Required,
            false),

        new AllowedCommand(
            "ipconfig.exe",
            "Tools.IpConfig.Description",
            new[] { "/flushdns", "/displaydns", "/all" },
            false,
            ElevationRequirement.None,
            false),

        new AllowedCommand(
            "cleanmgr.exe",
            "Tools.CleanMgr.Description",
            new[] { "/d", "/sagerun:1", "/lowdisk" },
            true,
            ElevationRequirement.None,
            false),

        new AllowedCommand(
            "schtasks.exe",
            "Tools.SchTasks.Description",
            new[] { "/create", "/delete", "/query", "/tn", "/tr", "/sc", "/st", "/d", "/f" },
            false,
            ElevationRequirement.None,
            false,
            new[]
            {
                // Nome da tarefa: apenas caracteres seguros, sem espacos nem aspas.
                new ArgumentValueRule("/tn", "[A-Za-z0-9][A-Za-z0-9._-]{0,63}"),

                // Acao da tarefa: somente o proprio executavel do aplicativo e, no maximo,
                // um parametro simples. Nenhum caminho ou comando de terceiros e aceito.
                new ArgumentValueRule("/tr", "\"?%SELF%\"?(\\s+--[a-z][a-z-]{2,23})?"),

                // Frequencia e horario.
                new ArgumentValueRule("/sc", "DAILY|WEEKLY|MONTHLY"),
                new ArgumentValueRule("/st", "([01][0-9]|2[0-3]):[0-5][0-9]"),

                // Dia da semana ou dia do mes.
                new ArgumentValueRule("/d", "MON|TUE|WED|THU|FRI|SAT|SUN|([1-9]|[12][0-9]|3[01])")
            })
    };

    public static bool TryResolve(string executablePath, IReadOnlyList<string> arguments, out AllowedCommand? command)
    {
        command = null;

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var fullPath = ProtectedPaths.Normalize(executablePath);
        if (fullPath.Length == 0 || File.Exists(fullPath) is false)
        {
            return false;
        }

        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system32 = Path.Combine(systemRoot, "System32");
        if (ProtectedPaths.IsUnder(fullPath, system32) is false)
        {
            return false;
        }

        var fileName = Path.GetFileName(fullPath);
        var match = All.FirstOrDefault(c => string.Equals(c.Executable, fileName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        for (var index = 0; index < arguments.Count; index++)
        {
            var trimmed = arguments[index]?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return false;
            }

            var isAllowedToken = match.AllowedArguments.Contains(trimmed!, StringComparer.OrdinalIgnoreCase);
            var isDriveLetter = match.AllowDriveLetterArgument && IsDriveLetter(trimmed!);
            var isRuleValue = index > 0 && MatchesValueRule(match, arguments[index - 1], trimmed!);
            if (isAllowedToken is false && isDriveLetter is false && isRuleValue is false)
            {
                return false;
            }
        }

        command = match;
        return true;
    }

    /// <summary>Resolve o caminho absoluto de um executavel autorizado dentro de System32.</summary>
    public static string ResolveExecutablePath(AllowedCommand command) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), command.Executable);

    /// <summary>
    /// Verifica se <paramref name="value"/> e um valor aceito para a opcao anterior.
    /// O tempo limite curto evita que uma expressao mal formada trave a validacao.
    /// </summary>
    private static bool MatchesValueRule(AllowedCommand command, string? previousArgument, string value)
    {
        if (command.ValueRules is not { Count: > 0 } rules || string.IsNullOrWhiteSpace(previousArgument))
        {
            return false;
        }

        var option = previousArgument.Trim();
        var rule = rules.FirstOrDefault(candidate =>
            string.Equals(candidate.Option, option, StringComparison.OrdinalIgnoreCase));

        if (rule is null)
        {
            return false;
        }

        try
        {
            var pattern = "\\A(?:" + Expand(rule.Pattern) + ")\\z";
            return Regex.IsMatch(
                value,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string Expand(string pattern)
    {
        if (pattern.Contains(Placeholder, StringComparison.Ordinal) is false)
        {
            return pattern;
        }

        var self = Environment.ProcessPath ?? string.Empty;
        return pattern.Replace(Placeholder, Regex.Escape(self), StringComparison.Ordinal);
    }

    private const string Placeholder = "%SELF%";

    private static bool IsDriveLetter(string value)
    {
        var trimmed = value.TrimEnd('\\', '/');
        if (trimmed.Length != 2 || trimmed[1] != ':')
        {
            return false;
        }

        return char.IsLetter(trimmed[0]);
    }
}
