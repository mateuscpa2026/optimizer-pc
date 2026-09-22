using System.Diagnostics;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Core.Security;

namespace OptimizerPC.Services.System;

/// <summary>
/// Ferramentas nativas do Windows. Nenhum comando vem da interface: as opcoes exibidas
/// sao derivadas da lista branca do aplicativo e revalidadas antes de cada execucao.
/// Comandos que exigem administrador sao informados como tal, sem tentativa silenciosa.
/// </summary>
public sealed class WindowsToolsService : IWindowsToolsService
{
    private static readonly ToolMetadata[] Metadata =
    {
        new("sfc", "Tools.Sfc.Title", false, null, new[] { "/scannow" }),
        new("dism", "Tools.Dism.Title", true, "Tools.Dism.Note", new[] { "/online", "/cleanup-image", "/scanhealth" }),
        new("chkdsk", "Tools.Chkdsk.Title", true, "Tools.Chkdsk.Note", new[] { "/scan", "{systemdrive}" }),
        new("defrag", "Tools.Defrag.Title", true, "Tools.Defrag.Note", new[] { "/o", "{systemdrive}" }),
        new("powercfg", "Tools.Powercfg.Title", false, null, new[] { "/list" }),
        new("ipconfig", "Tools.Ipconfig.Title", false, null, new[] { "/all" }),
        new("cleanmgr", "Tools.Cleanmgr.Title", false, "Tools.Cleanmgr.Note", Array.Empty<string>()),
        new("schtasks", "Tools.Schtasks.Title", true, "Tools.Schtasks.Note", new[] { "/query" })
    };

    private const string DrivePlaceholder = "{systemdrive}";

    private readonly IElevationService _elevation;
    private readonly ICommandExecutionService _commands;
    private readonly IAppLogger _logger;

    public WindowsToolsService(IElevationService elevation, ICommandExecutionService commands, IAppLogger logger)
    {
        _elevation = elevation;
        _commands = commands;
        _logger = logger;
    }

    public IReadOnlyList<WindowsToolDescriptor> GetTools()
    {
        var drive = GetSystemDriveArgument();
        var tools = new List<WindowsToolDescriptor>();

        foreach (var metadata in Metadata)
        {
            var command = CommandAllowList.All.FirstOrDefault(c =>
                string.Equals(Path.GetFileNameWithoutExtension(c.Executable), metadata.Id, StringComparison.OrdinalIgnoreCase));

            if (command is null)
            {
                continue;
            }

            tools.Add(new WindowsToolDescriptor
            {
                Id = metadata.Id,
                TitleKey = metadata.TitleKey,
                DescriptionKey = command.DescriptionKey,
                Command = command.Executable,
                DefaultArguments = metadata.DefaultArguments
                    .Select(argument => string.Equals(argument, DrivePlaceholder, StringComparison.Ordinal) ? drive : argument)
                    .ToArray(),
                Elevation = command.Elevation,
                IsAdvanced = metadata.IsAdvanced,
                NoteKey = metadata.NoteKey
            });
        }

        return tools;
    }

    public async Task<CommandResult> RunAsync(
        string toolId,
        IEnumerable<string>? extraArguments = null,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        var tool = GetTools().FirstOrDefault(t => string.Equals(t.Id, toolId, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
        {
            return new CommandResult(false, -1, string.Empty, string.Empty, "Tools.Error.NotFound");
        }

        var command = CommandAllowList.All.FirstOrDefault(c =>
            string.Equals(Path.GetFileNameWithoutExtension(c.Executable), tool.Id, StringComparison.OrdinalIgnoreCase));

        if (command is null)
        {
            return new CommandResult(false, -1, string.Empty, string.Empty, "Tools.Error.NotFound");
        }

        var arguments = BuildArguments(tool, extraArguments);
        var executable = CommandAllowList.ResolveExecutablePath(command);

        if (CommandAllowList.TryResolve(executable, arguments, out _) is false)
        {
            _logger.Warning("Tools", "Comando recusado pela lista branca na ferramenta " + tool.Id + ".");
            return new CommandResult(false, -1, string.Empty, string.Empty, "Tools.Error.NotAllowed");
        }

        if (tool.Elevation is ElevationRequirement.Required && _elevation.IsElevated is false)
        {
            _logger.Info("Tools", tool.Command + " exige privilegios administrativos.");
            return new CommandResult(false, -1, string.Empty, string.Empty, "Tools.Error.NeedsElevation");
        }

        var result = await _commands.RunAllowedAsync(executable, arguments, cancellationToken).ConfigureAwait(false);

        // A captura de saida do processo e feita por completo antes de publicar: o servico
        // de execucao nao transmite a saida em tempo real.
        Publish(output, result.StandardOutput);
        Publish(output, result.StandardError);

        return result;
    }

    private static IReadOnlyList<string> BuildArguments(WindowsToolDescriptor tool, IEnumerable<string>? extraArguments)
    {
        var arguments = new List<string>(tool.DefaultArguments);

        if (extraArguments is null)
        {
            return arguments;
        }

        foreach (var extra in extraArguments)
        {
            if (string.IsNullOrWhiteSpace(extra) is false)
            {
                arguments.Add(extra.Trim());
            }
        }

        return arguments;
    }

    private static void Publish(IProgress<string>? output, string text)
    {
        if (output is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r', ' ', '\t');
            if (trimmed.Length > 0)
            {
                output.Report(trimmed);
            }
        }
    }

    private static string GetSystemDriveArgument()
    {
        var root = VolumeEnumerator.GetSystemDriveRoot();
        return root.TrimEnd('\\');
    }

    private sealed record ToolMetadata(string Id, string TitleKey, bool IsAdvanced, string? NoteKey, string[] DefaultArguments);
}
