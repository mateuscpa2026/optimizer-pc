using System.Text;

namespace OptimizerPC.Services.Reports;

/// <summary>
/// Renderizacao em CSV no formato longo: uma linha por informacao, com cinco colunas
/// fixas (secao, tabela, linha, coluna, valor). Esse formato abre corretamente em
/// planilhas e nao depende da quantidade de colunas de cada tabela.
/// </summary>
internal static class CsvReportWriter
{
    internal const char Separator = ';';

    internal const string Header = "Secao;Tabela;Linha;Coluna;Valor";

    internal static string Write(ReportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Header);
        builder.Append(Escape(document.Title)).Append(Separator)
            .Append(Separator).Append('1').Append(Separator)
            .Append(Escape(document.GeneratedAtLabel)).Append(Separator)
            .Append(Escape(document.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")))
            .AppendLine();

        foreach (var section in document.Sections)
        {
            if (section.IsEmpty)
            {
                continue;
            }

            foreach (var field in section.Fields)
            {
                Append(builder, section.Title, string.Empty, string.Empty, field.Label, field.Value);
            }

            foreach (var table in section.Tables)
            {
                var tableName = table.Title ?? string.Empty;
                for (var index = 0; index < table.Rows.Count; index++)
                {
                    var row = table.Rows[index];
                    var rowNumber = (index + 1).ToString();
                    for (var column = 0; column < table.Headers.Count; column++)
                    {
                        var cell = column < row.Count ? row[column] : string.Empty;
                        Append(builder, section.Title, tableName, rowNumber, table.Headers[column], cell);
                    }
                }
            }
        }

        return builder.ToString();
    }

    private static void Append(
        StringBuilder builder,
        string section,
        string table,
        string row,
        string column,
        string value)
    {
        builder.Append(Escape(section)).Append(Separator)
            .Append(Escape(table)).Append(Separator)
            .Append(Escape(row)).Append(Separator)
            .Append(Escape(column)).Append(Separator)
            .Append(Escape(value))
            .AppendLine();
    }

    internal static string Escape(string? value)
    {
        var text = ReportTextLayout.SingleLine(value);
        if (text.IndexOfAny(new[] { Separator, '"' }) < 0)
        {
            return text;
        }

        return '"' + text.Replace("\"", "\"\"") + '"';
    }
}
