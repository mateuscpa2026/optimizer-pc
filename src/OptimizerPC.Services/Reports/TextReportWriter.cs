using System.Globalization;
using System.Text;

namespace OptimizerPC.Services.Reports;

/// <summary>Renderizacao em texto puro. Base tambem do PDF, que usa a mesma fonte monoespacada.</summary>
internal static class TextReportWriter
{
    private const int Width = ReportTextLayout.MaxWidth;

    internal static string Write(ReportDocument document)
    {
        var builder = new StringBuilder();

        builder.AppendLine(Rule('='));
        foreach (var line in ReportTextLayout.Wrap(document.Title, Width))
        {
            builder.AppendLine(line);
        }

        builder.AppendLine(
            ReportTextLayout.SingleLine(document.GeneratedAtLabel) + ": " +
            document.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        builder.AppendLine(Rule('='));

        if (document.Introduction.Length > 0)
        {
            builder.AppendLine();
            foreach (var line in ReportTextLayout.Wrap(document.Introduction, Width))
            {
                builder.AppendLine(line);
            }
        }

        foreach (var section in document.Sections)
        {
            if (section.IsEmpty)
            {
                continue;
            }

            builder.AppendLine();
            builder.AppendLine(Rule('-'));
            builder.AppendLine(ReportTextLayout.SingleLine(section.Title).ToUpperInvariant());
            builder.AppendLine(Rule('-'));

            foreach (var field in section.Fields)
            {
                foreach (var line in ReportTextLayout.Field(field.Label, field.Value))
                {
                    builder.AppendLine(line);
                }
            }

            foreach (var table in section.Tables)
            {
                if (string.IsNullOrWhiteSpace(table.Title) is false)
                {
                    builder.AppendLine();
                    builder.AppendLine(ReportTextLayout.SingleLine(table.Title));
                }

                foreach (var line in ReportTextLayout.Table(table))
                {
                    builder.AppendLine(line);
                }
            }

            if (string.IsNullOrWhiteSpace(section.Note) is false)
            {
                builder.AppendLine();
                foreach (var line in ReportTextLayout.Wrap(section.Note, Width, 2))
                {
                    builder.AppendLine(line);
                }
            }
        }

        if (document.Footer.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine(Rule('-'));
            foreach (var line in ReportTextLayout.Wrap(document.Footer, Width))
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString();
    }

    private static string Rule(char character) => new(character, Width);
}
