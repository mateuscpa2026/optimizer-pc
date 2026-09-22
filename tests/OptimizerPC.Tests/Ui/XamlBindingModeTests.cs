using Xunit;

namespace OptimizerPC.Tests.Ui;

/// <summary>
/// ProgressBar.Value, CheckBox.IsChecked e ComboBox.SelectedItem sao de duas vias por
/// padrao no WPF. Ligar um desses alvos a uma propriedade sem setter publico derruba a
/// tela com InvalidOperationException assim que o vinculo e aplicado, por isso cada
/// vinculo desse tipo precisa declarar Mode=OneWay.
/// </summary>
public sealed class XamlBindingModeTests
{
    /// <summary>Alvos cujo modo padrao de vinculo e duas vias.</summary>
    private static readonly string[] TwoWayByDefaultTargets =
    {
        "Value", "IsChecked", "SelectedItem", "SelectedValue", "SelectedIndex"
    };

    /// <summary>
    /// Propriedades calculadas da camada de apresentacao, sem setter publico. Ao exibir
    /// outra propriedade desse tipo em um alvo de duas vias, inclua o nome nesta lista.
    /// </summary>
    private static readonly string[] DisplayOnlySources =
    {
        "ProgressPercent", "UsedPercent", "SharePercent"
    };

    [Fact]
    public void VinculosDeDuasVias_NaoApontamParaPropriedadesSomenteLeitura()
    {
        var problems = new List<string>();

        foreach (var file in XamlFiles())
        {
            var number = 0;

            foreach (var line in File.ReadLines(file))
            {
                number++;

                foreach (var target in TwoWayByDefaultTargets)
                {
                    var start = line.IndexOf(target + "=\"{Binding", StringComparison.Ordinal);

                    if (start < 0 || line[start..].Contains("OneWay", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var source in DisplayOnlySources.Where(source => BindsTo(line[start..], source)))
                    {
                        problems.Add(Relative(file) + ":" + number + ": " + target + " -> " + source);
                    }
                }
            }
        }

        Assert.Empty(problems);
    }

    /// <summary>Verdadeiro quando o caminho do vinculo termina na propriedade indicada.</summary>
    private static bool BindsTo(string binding, string source) =>
        binding.Contains(source + ",", StringComparison.Ordinal) ||
        binding.Contains(source + "}", StringComparison.Ordinal);

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src", "OptimizerPC.App"), "*.xaml", SearchOption.AllDirectories)
            .Where(file => file.Contains(@"\bin\") is false && file.Contains(@"\obj\") is false);

    private static string Relative(string file) =>
        Path.GetRelativePath(RepositoryRoot(), file).Replace('\\', '/');

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OptimizerPC.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Raiz do repositorio nao encontrada a partir de " + AppContext.BaseDirectory);
    }
}
