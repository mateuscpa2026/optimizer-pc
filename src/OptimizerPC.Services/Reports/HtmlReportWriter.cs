using System.Globalization;
using System.Text;

namespace OptimizerPC.Services.Reports;

/// <summary>
/// Renderizacao em HTML. O arquivo e totalmente autonomo: o CSS vai embutido e
/// nenhuma fonte, imagem ou script externo e referenciado.
/// </summary>
internal static class HtmlReportWriter
{
    internal static string Write(ReportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"" + Escape(document.LanguageCode) + "\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\" />");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine("<title>" + Escape(document.Title) + "</title>");
        builder.AppendLine(Style);
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main class=\"page\">");

        builder.AppendLine("  <header>");
        builder.AppendLine("    <p class=\"brand\">Optimizer PC</p>");
        builder.AppendLine("    <h1>" + Escape(document.Title) + "</h1>");
        builder.AppendLine(
            "    <p class=\"stamp\">" + Escape(document.GeneratedAtLabel) + ": " +
            Escape(document.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)) +
            "</p>");
        if (document.Introduction.Length > 0)
        {
            builder.AppendLine("    <p class=\"intro\">" + Escape(document.Introduction) + "</p>");
        }

        builder.AppendLine("  </header>");

        foreach (var section in document.Sections)
        {
            if (section.IsEmpty)
            {
                continue;
            }

            builder.AppendLine("  <section>");
            builder.AppendLine("    <h2>" + Escape(section.Title) + "</h2>");

            if (section.Fields.Count > 0)
            {
                builder.AppendLine("    <table class=\"fields\">");
                builder.AppendLine("      <tbody>");
                foreach (var field in section.Fields)
                {
                    builder.AppendLine(
                        "        <tr><th scope=\"row\">" + Escape(field.Label) + "</th><td>" + Escape(field.Value) + "</td></tr>");
                }

                builder.AppendLine("      </tbody>");
                builder.AppendLine("    </table>");
            }

            foreach (var table in section.Tables)
            {
                if (string.IsNullOrWhiteSpace(table.Title) is false)
                {
                    builder.AppendLine("    <h3>" + Escape(table.Title) + "</h3>");
                }

                builder.AppendLine("    <table class=\"grid\">");
                builder.AppendLine("      <thead><tr>");
                foreach (var header in table.Headers)
                {
                    builder.AppendLine("        <th>" + Escape(header) + "</th>");
                }

                builder.AppendLine("      </tr></thead>");
                builder.AppendLine("      <tbody>");

                if (table.Rows.Count == 0)
                {
                    builder.AppendLine(
                        "        <tr><td colspan=\"" + Math.Max(1, table.Headers.Count) + "\" class=\"empty\">" +
                        Escape(table.EmptyText) + "</td></tr>");
                }

                foreach (var row in table.Rows)
                {
                    builder.AppendLine("        <tr>");
                    for (var index = 0; index < table.Headers.Count; index++)
                    {
                        var cell = index < row.Count ? row[index] : string.Empty;
                        builder.AppendLine("          <td>" + Escape(cell) + "</td>");
                    }

                    builder.AppendLine("        </tr>");
                }

                builder.AppendLine("      </tbody>");
                builder.AppendLine("    </table>");
            }

            if (string.IsNullOrWhiteSpace(section.Note) is false)
            {
                builder.AppendLine("    <p class=\"note\">" + Escape(section.Note) + "</p>");
            }

            builder.AppendLine("  </section>");
        }

        if (document.Footer.Length > 0)
        {
            builder.AppendLine("  <footer>" + Escape(document.Footer) + "</footer>");
        }

        builder.AppendLine("</main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    internal static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 16);
        foreach (var character in value)
        {
            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"':
                    builder.Append("&quot;");
                    break;
                case '\'':
                    builder.Append("&#39;");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    private const string Style = """
<style>
  :root {
    color-scheme: light;
    --ink: #10233a;
    --muted: #5a6b80;
    --line: #d8e0ea;
    --accent: #1f6feb;
    --surface: #ffffff;
    --surface-alt: #f5f8fc;
  }
  * { box-sizing: border-box; }
  body {
    margin: 0;
    padding: 32px 16px;
    background: var(--surface-alt);
    color: var(--ink);
    font-family: "Segoe UI", "Inter", system-ui, -apple-system, sans-serif;
    font-size: 14px;
    line-height: 1.5;
  }
  .page {
    max-width: 960px;
    margin: 0 auto;
    background: var(--surface);
    border: 1px solid var(--line);
    border-radius: 12px;
    padding: 32px;
    box-shadow: 0 8px 24px rgba(16, 35, 58, 0.06);
  }
  header { border-bottom: 2px solid var(--accent); padding-bottom: 16px; margin-bottom: 24px; }
  .brand { margin: 0; font-size: 12px; letter-spacing: 0.16em; text-transform: uppercase; color: var(--accent); font-weight: 600; }
  h1 { margin: 8px 0 4px; font-size: 26px; }
  .stamp { margin: 0; color: var(--muted); font-size: 13px; }
  .intro { margin: 12px 0 0; color: var(--muted); }
  section { margin-bottom: 28px; }
  h2 { font-size: 17px; margin: 0 0 12px; padding-bottom: 6px; border-bottom: 1px solid var(--line); }
  h3 { font-size: 14px; margin: 18px 0 8px; color: var(--muted); text-transform: uppercase; letter-spacing: 0.06em; }
  table { width: 100%; border-collapse: collapse; }
  table.fields th {
    width: 34%;
    text-align: left;
    font-weight: 600;
    color: var(--muted);
    padding: 6px 12px 6px 0;
    vertical-align: top;
    border-bottom: 1px solid var(--line);
  }
  table.fields td { padding: 6px 0; border-bottom: 1px solid var(--line); word-break: break-word; }
  table.grid { font-size: 13px; margin-top: 4px; }
  table.grid th, table.grid td { border: 1px solid var(--line); padding: 6px 8px; text-align: left; }
  table.grid thead th { background: var(--surface-alt); font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em; color: var(--muted); }
  table.grid tbody tr:nth-child(even) { background: #fafcfe; }
  td.empty { color: var(--muted); font-style: italic; }
  .note { margin: 10px 0 0; padding: 10px 12px; border-left: 3px solid var(--accent); background: var(--surface-alt); color: var(--muted); font-size: 13px; }
  footer { margin-top: 24px; padding-top: 14px; border-top: 1px solid var(--line); color: var(--muted); font-size: 12px; }
  @media print {
    body { background: #fff; padding: 0; }
    .page { border: 0; box-shadow: none; border-radius: 0; padding: 0; max-width: none; }
    section { break-inside: avoid; }
  }
</style>
""";
}
