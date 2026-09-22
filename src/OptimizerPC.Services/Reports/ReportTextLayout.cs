using System.Text;

namespace OptimizerPC.Services.Reports;

/// <summary>
/// Formatacao em texto puro. O mesmo layout alimenta o arquivo .txt e o PDF
/// (fonte monoespaçada), garantindo que os dois formatos mostrem o mesmo conteudo.
/// </summary>
internal static class ReportTextLayout
{
    internal const int MaxWidth = 96;

    internal const int LabelWidth = 30;

    /// <summary>Remove caracteres de controle e normaliza o texto para uma linha.</summary>
    internal static string SingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '\r' or '\n' or '\t')
            {
                builder.Append(' ');
                continue;
            }

            if (char.IsControl(character))
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString().Trim();
    }

    /// <summary>Quebra o texto em linhas de ate <paramref name="width"/> caracteres.</summary>
    internal static IReadOnlyList<string> Wrap(string? value, int width, int indent = 0)
    {
        var text = SingleLine(value);
        if (text.Length == 0)
        {
            return new[] { string.Empty };
        }

        var prefix = new string(' ', Math.Max(0, indent));
        var available = Math.Max(16, width - prefix.Length);
        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > available)
            {
                lines.Add(prefix + current);
                current.Clear();
            }

            if (word.Length > available)
            {
                if (current.Length > 0)
                {
                    lines.Add(prefix + current);
                    current.Clear();
                }

                for (var offset = 0; offset < word.Length; offset += available)
                {
                    var slice = word.Substring(offset, Math.Min(available, word.Length - offset));
                    if (offset + available < word.Length)
                    {
                        lines.Add(prefix + slice);
                    }
                    else
                    {
                        current.Append(slice);
                    }
                }

                continue;
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > 0)
        {
            lines.Add(prefix + current);
        }

        return lines.Count == 0 ? new[] { prefix } : lines;
    }

    internal static IReadOnlyList<string> Field(string label, string value)
    {
        var name = SingleLine(label);
        var padded = name.Length >= LabelWidth
            ? name[..LabelWidth]
            : name.PadRight(LabelWidth);

        var lines = Wrap(value, MaxWidth, LabelWidth + 3);
        var result = new List<string>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var content = lines[index].TrimStart();
            result.Add(index == 0 ? padded + " : " + content : new string(' ', LabelWidth + 3) + content);
        }

        return result;
    }

    /// <summary>
    /// Monta uma tabela com colunas alinhadas. Larguras sao calculadas a partir do
    /// conteudo e reduzidas proporcionalmente quando o total passa da largura maxima.
    /// </summary>
    internal static IReadOnlyList<string> Table(ReportTable table)
    {
        var columnCount = table.Headers.Count;
        if (columnCount == 0)
        {
            return Array.Empty<string>();
        }

        var rows = new List<string[]>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            var cells = new string[columnCount];
            for (var index = 0; index < columnCount; index++)
            {
                cells[index] = index < row.Count ? SingleLine(row[index]) : string.Empty;
            }

            rows.Add(cells);
        }

        var widths = new int[columnCount];
        for (var index = 0; index < columnCount; index++)
        {
            widths[index] = SingleLine(table.Headers[index]).Length;
        }

        foreach (var row in rows)
        {
            for (var index = 0; index < columnCount; index++)
            {
                widths[index] = Math.Max(widths[index], row[index].Length);
            }
        }

        Shrink(widths);

        var lines = new List<string>();
        lines.Add(BuildRow(table.Headers, widths));
        lines.Add(string.Join("-+-", widths.Select(width => new string('-', width))));

        if (rows.Count == 0)
        {
            lines.Add(SingleLine(table.EmptyText));
            return lines;
        }

        foreach (var row in rows)
        {
            lines.Add(BuildRow(row, widths));
        }

        return lines;
    }

    private static void Shrink(int[] widths)
    {
        const int separator = 3;
        var total = widths.Sum() + (separator * (widths.Length - 1));
        if (total <= MaxWidth)
        {
            return;
        }

        var minimum = 8;
        while (total > MaxWidth)
        {
            var widest = 0;
            for (var index = 1; index < widths.Length; index++)
            {
                if (widths[index] > widths[widest])
                {
                    widest = index;
                }
            }

            if (widths[widest] <= minimum)
            {
                break;
            }

            widths[widest]--;
            total--;
        }
    }

    private static string BuildRow(IReadOnlyList<string> cells, int[] widths)
    {
        var parts = new List<string>(widths.Length);
        for (var index = 0; index < widths.Length; index++)
        {
            var value = index < cells.Count ? SingleLine(cells[index]) : string.Empty;
            parts.Add(value.Length > widths[index] ? value[..widths[index]] : value.PadRight(widths[index]));
        }

        return string.Join(" | ", parts).TrimEnd();
    }
}
