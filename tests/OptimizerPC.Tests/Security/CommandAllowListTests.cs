using OptimizerPC.Core;
using OptimizerPC.Core.Security;
using Xunit;

namespace OptimizerPC.Tests.Security;

/// <summary>
/// Prova que o aplicativo so executa os executaveis nativos da lista branca, a partir
/// de System32, com argumentos previamente autorizados. Nenhum comando arbitrario passa.
/// </summary>
public sealed class CommandAllowListTests
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private static string? System32Path(string executable)
    {
        var command = CommandAllowList.All.FirstOrDefault(item =>
            string.Equals(item.Executable, executable, StringComparison.OrdinalIgnoreCase));

        if (command is null)
        {
            return null;
        }

        var path = CommandAllowList.ResolveExecutablePath(command);
        return File.Exists(path) ? path : null;
    }

    private static bool TryResolve(string? executablePath, params string[] arguments)
    {
        if (executablePath is null)
        {
            // Executavel ausente na maquina de teste: a regra de argumentos nao pode ser exercitada.
            return false;
        }

        return CommandAllowList.TryResolve(executablePath, arguments, out _);
    }

    [Fact]
    public void All_ContemSomenteOsComandosDeManutencaoPrevistos()
    {
        var expected = new[] { "sfc.exe", "dism.exe", "chkdsk.exe", "powercfg.exe", "defrag.exe", "ipconfig.exe", "cleanmgr.exe", "schtasks.exe" };

        Assert.Equal(expected.Length, CommandAllowList.All.Count);
        Assert.Equal(
            expected.OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            CommandAllowList.All.Select(command => command.Executable).OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void All_NaoAutorizaAlteracaoDeSegurancaNemOverclock()
    {
        var executables = CommandAllowList.All.Select(command => command.Executable).ToList();

        foreach (var forbidden in new[] { "cmd.exe", "powershell.exe", "reg.exe", "regedit.exe", "netsh.exe", "bcdedit.exe", "wmic.exe", "sc.exe", "taskkill.exe", "msconfig.exe" })
        {
            Assert.DoesNotContain(forbidden, executables, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void All_NaoAutorizaDesativarDefenderNemFirewall()
    {
        foreach (var command in CommandAllowList.All)
        {
            foreach (var argument in command.AllowedArguments)
            {
                Assert.DoesNotContain("defender", argument, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("firewall", argument, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("disable", argument, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void All_MarcaLeituraComoSomenteLeituraEExigeElevacaoQuandoNecessario()
    {
        var chkdsk = CommandAllowList.All.Single(command => command.Executable == "chkdsk.exe");
        var powercfg = CommandAllowList.All.Single(command => command.Executable == "powercfg.exe");
        var sfc = CommandAllowList.All.Single(command => command.Executable == "sfc.exe");

        Assert.True(chkdsk.IsReadOnly);
        Assert.Equal(ElevationRequirement.Required, chkdsk.Elevation);
        Assert.Equal(ElevationRequirement.None, powercfg.Elevation);
        Assert.Equal(ElevationRequirement.Required, sfc.Elevation);
    }

    [Fact]
    public void TryResolve_RecusaCaminhoVazio()
    {
        Assert.False(CommandAllowList.TryResolve(string.Empty, Array.Empty<string>(), out _));
        Assert.False(CommandAllowList.TryResolve("   ", Array.Empty<string>(), out _));
    }

    [Fact]
    public void TryResolve_RecusaExecutavelForaDoSystem32()
    {
        Assert.False(CommandAllowList.TryResolve(Path.Combine(Path.GetTempPath(), "ipconfig.exe"), Array.Empty<string>(), out _));
        Assert.False(CommandAllowList.TryResolve("ipconfig.exe", Array.Empty<string>(), out _));
    }

    [Fact]
    public void TryResolve_RecusaExecutavelQueNaoEstaNaListaBranca()
    {
        var explorer = Path.Combine(Windows, "explorer.exe");
        if (File.Exists(explorer) is false)
        {
            return;
        }

        Assert.False(CommandAllowList.TryResolve(explorer, Array.Empty<string>(), out _));
    }

    [Fact]
    public void TryResolve_RecusaComandoComArgumentoDesconhecido()
    {
        var ipconfig = System32Path("ipconfig.exe");

        Assert.False(TryResolve(ipconfig, "/release"));
        Assert.False(TryResolve(ipconfig, "/flushdns", "&&", "calc.exe"));
        Assert.False(TryResolve(ipconfig, "/displaydns", ";"));
    }

    [Fact]
    public void TryResolve_RecusaArgumentoVazio()
    {
        var ipconfig = System32Path("ipconfig.exe");

        Assert.False(TryResolve(ipconfig, "/flushdns", "   "));
    }

    [Fact]
    public void TryResolve_AceitaComandosDeLeituraConhecidos()
    {
        var ipconfig = System32Path("ipconfig.exe");
        var powercfg = System32Path("powercfg.exe");

        if (ipconfig is null || powercfg is null)
        {
            return;
        }

        Assert.True(CommandAllowList.TryResolve(ipconfig, new[] { "/flushdns" }, out _));
        Assert.True(CommandAllowList.TryResolve(powercfg, new[] { "/list" }, out _));
        Assert.True(CommandAllowList.TryResolve(powercfg, new[] { "/getactivescheme" }, out _));
    }

    [Fact]
    public void TryResolve_RecusaTrocaDePlanoDeEnergiaComGuidArbitrario()
    {
        var powercfg = System32Path("powercfg.exe");
        if (powercfg is null)
        {
            return;
        }

        // /setactive existe na lista, mas nao ha regra de valor: nenhum GUID e aceito.
        Assert.False(CommandAllowList.TryResolve(powercfg, new[] { "/setactive", "381b4222-f694-41f0-9685-ff5bb260df2e" }, out _));
    }

    [Fact]
    public void TryResolve_SoAceitaLetraDeUnidadeOndeFoiAutorizado()
    {
        var chkdsk = System32Path("chkdsk.exe");
        var sfc = System32Path("sfc.exe");

        if (chkdsk is null || sfc is null)
        {
            return;
        }

        Assert.True(CommandAllowList.TryResolve(chkdsk, new[] { "/scan", "C:" }, out _));
        Assert.True(CommandAllowList.TryResolve(chkdsk, new[] { "/scan", "D:\\" }, out _));
        Assert.False(CommandAllowList.TryResolve(chkdsk, new[] { "/scan", "1:" }, out _));
        Assert.False(CommandAllowList.TryResolve(chkdsk, new[] { "/scan", "C:\\Windows" }, out _));
        Assert.False(CommandAllowList.TryResolve(sfc, new[] { "/scannow", "C:" }, out _));
    }

    [Fact]
    public void TryResolve_RecusaNomeDeTarefaComCaracteresPerigosos()
    {
        var schtasks = System32Path("schtasks.exe");
        if (schtasks is null)
        {
            return;
        }

        Assert.True(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/tn", "OptimizerPC-Manutencao" }, out _));
        Assert.False(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/tn", "nome com espaco" }, out _));
        Assert.False(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/tn", "nome&calc" }, out _));
        Assert.False(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/tn", "\"aspas\"" }, out _));
    }

    [Fact]
    public void TryResolve_SoAceitaOProprioExecutavelNaAcaoDaTarefa()
    {
        var schtasks = System32Path("schtasks.exe");
        var self = Environment.ProcessPath;

        if (schtasks is null || string.IsNullOrWhiteSpace(self))
        {
            return;
        }

        Assert.True(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/tr", self }, out _));
        Assert.True(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/tr", "\"" + self + "\"" }, out _));
        Assert.True(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/tr", self + " --manutencao" }, out _));
        Assert.False(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/tr", self + "extra" }, out _));
        Assert.False(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/tr", self + " /x" }, out _));
        Assert.False(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/tr", "calc.exe" }, out _));
    }

    [Fact]
    public void TryResolve_ValidaFrequenciaHorarioEDiaDaTarefa()
    {
        var schtasks = System32Path("schtasks.exe");
        if (schtasks is null)
        {
            return;
        }

        Assert.True(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/sc", "DAILY", "/st", "07:30" }, out _));
        Assert.True(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/sc", "weekly", "/d", "MON" }, out _));
        Assert.True(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/sc", "MONTHLY", "/d", "15" }, out _));

        Assert.False(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/sc", "DIARIO" }, out _));
        Assert.False(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/sc", "DAILY", "/st", "25:00" }, out _));
        Assert.False(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/sc", "WEEKLY", "/d", "32" }, out _));
        Assert.False(CommandAllowList.TryResolve(schtasks, new[] { "/create", "/sc", "WEEKLY", "/d", "SEG" }, out _));
    }

    [Fact]
    public void TryResolve_NaoAceitaValorDeRegraSemAOpcaoAnterior()
    {
        var schtasks = System32Path("schtasks.exe");
        if (schtasks is null)
        {
            return;
        }

        // "DAILY" e um valor valido, mas apenas logo apos /sc.
        Assert.False(CommandAllowList.TryResolve(schtasks, new[] { "DAILY" }, out _));
        Assert.False(CommandAllowList.TryResolve(schtasks, new[] { "/f", "DAILY" }, out _));
    }

    [Fact]
    public void TryResolve_AceitaSomenteLeituraDeIntegridadeDeDisco()
    {
        var sfc = System32Path("sfc.exe");
        var dism = System32Path("dism.exe");

        if (sfc is null || dism is null)
        {
            return;
        }

        Assert.True(CommandAllowList.TryResolve(sfc, new[] { "/verifyonly" }, out _));
        Assert.True(CommandAllowList.TryResolve(dism, new[] { "/online", "/cleanup-image", "/scanhealth" }, out _));
        Assert.False(CommandAllowList.TryResolve(dism, new[] { "/online", "/mount-image", "/imagefile:C:\\x.wim" }, out _));
    }

    [Fact]
    public void ResolveExecutablePath_ApontaParaODiretorioDoSistema()
    {
        var sfc = CommandAllowList.All.Single(command => command.Executable == "sfc.exe");
        var path = CommandAllowList.ResolveExecutablePath(sfc);
        var system32 = Path.Combine(Windows, "System32");

        Assert.True(Path.IsPathFullyQualified(path));
        Assert.True(ProtectedPaths.IsUnder(path, system32), "O executavel resolvido deveria estar em System32: " + path);
    }
}
