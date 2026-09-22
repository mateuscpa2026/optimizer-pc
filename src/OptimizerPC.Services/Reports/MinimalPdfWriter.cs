using System.Globalization;
using System.Text;

namespace OptimizerPC.Services.Reports;

/// <summary>
/// Gerador de PDF 1.4 proprio, sem dependencia externa. Usa a fonte base Courier
/// (monoespaçada, disponivel em qualquer leitor) para preservar o alinhamento das
/// tabelas do relatorio. O texto e gravado em Latin-1, suficiente para o portugues.
/// </summary>
internal static class MinimalPdfWriter
{
    private const double PageWidth = 595.28;
    private const double PageHeight = 841.89;
    private const double Margin = 40;
    private const double FontSize = 8.6;
    private const double Leading = 11.6;
    private const int LinesPerPage = 64;

    internal static byte[] Write(string title, string text)
    {
        var lines = SplitLines(text);
        var pages = Paginate(lines);

        using var stream = new MemoryStream();
        var offsets = new Dictionary<int, long>();
        var nextObject = 1;

        var catalogId = nextObject++;
        var pagesId = nextObject++;
        var fontId = nextObject++;
        var infoId = nextObject++;

        var pageIds = new List<int>(pages.Count);
        var contentIds = new List<int>(pages.Count);
        for (var index = 0; index < pages.Count; index++)
        {
            pageIds.Add(nextObject++);
            contentIds.Add(nextObject++);
        }

        Write(stream, "%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");

        AddObject(stream, offsets, catalogId, "<< /Type /Catalog /Pages " + pagesId + " 0 R >>");
        AddObject(
            stream,
            offsets,
            pagesId,
            "<< /Type /Pages /Count " + pages.Count + " /Kids [" +
            string.Join(' ', pageIds.Select(id => id + " 0 R")) + "] >>");
        AddObject(
            stream,
            offsets,
            fontId,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
        AddObject(
            stream,
            offsets,
            infoId,
            "<< /Title (" + EscapeText(title) + ") /Producer (Optimizer PC) /Creator (Optimizer PC) >>");

        for (var index = 0; index < pages.Count; index++)
        {
            var content = BuildContent(pages[index]);
            AddObject(
                stream,
                offsets,
                pageIds[index],
                "<< /Type /Page /Parent " + pagesId + " 0 R /MediaBox [0 0 " +
                Number(PageWidth) + " " + Number(PageHeight) + "] /Resources << /Font << /F1 " +
                fontId + " 0 R >> >> /Contents " + contentIds[index] + " 0 R >>");

            AddStreamObject(stream, offsets, contentIds[index], content);
        }

        var xrefOffset = stream.Position;
        WriteXref(stream, nextObject, offsets);
        Write(
            stream,
            "trailer\n<< /Size " + nextObject + " /Root " + catalogId + " 0 R /Info " + infoId +
            " 0 R >>\nstartxref\n" + xrefOffset.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");

        return stream.ToArray();
    }

    private static string BuildContent(string[] pageLines)
    {
        var builder = new StringBuilder();
        builder.Append("BT\n/F1 ").Append(Number(FontSize)).Append(" Tf\n")
            .Append(Number(Leading)).Append(" TL\n")
            .Append("1 0 0 1 ").Append(Number(Margin)).Append(' ')
            .Append(Number(PageHeight - Margin)).Append(" Tm\n");

        for (var index = 0; index < pageLines.Length; index++)
        {
            if (index > 0)
            {
                builder.Append("T*\n");
            }

            builder.Append('(').Append(EscapeText(pageLines[index])).Append(") Tj\n");
        }

        builder.Append("ET\n");
        return builder.ToString();
    }

    private static void AddObject(MemoryStream stream, Dictionary<int, long> offsets, int id, string body)
    {
        offsets[id] = stream.Position;
        Write(stream, id.ToString(CultureInfo.InvariantCulture) + " 0 obj\n" + body + "\nendobj\n");
    }

    private static void AddStreamObject(MemoryStream stream, Dictionary<int, long> offsets, int id, string content)
    {
        var bytes = Encoding.Latin1.GetBytes(content);
        offsets[id] = stream.Position;
        Write(
            stream,
            id.ToString(CultureInfo.InvariantCulture) + " 0 obj\n<< /Length " +
            bytes.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n");
        stream.Write(bytes, 0, bytes.Length);
        Write(stream, "\nendstream\nendobj\n");
    }

    private static void WriteXref(MemoryStream stream, int objectCount, Dictionary<int, long> offsets)
    {
        Write(stream, "xref\n0 " + objectCount.ToString(CultureInfo.InvariantCulture) + "\n");
        Write(stream, "0000000000 65535 f \r\n");

        for (var id = 1; id < objectCount; id++)
        {
            var offset = offsets.TryGetValue(id, out var value) ? value : 0;
            Write(stream, offset.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \r\n");
        }
    }

    private static IReadOnlyList<string[]> Paginate(IReadOnlyList<string> lines)
    {
        var pages = new List<string[]>();
        for (var offset = 0; offset < lines.Count; offset += LinesPerPage)
        {
            var count = Math.Min(LinesPerPage, lines.Count - offset);
            var page = new string[count];
            for (var index = 0; index < count; index++)
            {
                page[index] = lines[offset + index];
            }

            pages.Add(page);
        }

        if (pages.Count == 0)
        {
            pages.Add(Array.Empty<string>());
        }

        return pages;
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n').Select(static line => line.TrimEnd()).ToList();

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static string EscapeText(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (var character in value)
        {
            var mapped = Map(character);
            switch (mapped)
            {
                case '(':
                case ')':
                case '\\':
                    builder.Append('\\').Append(mapped);
                    break;
                default:
                    builder.Append(mapped);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Converte caracteres que nao existem no Latin-1 (aspas tipograficas, travessao,
    /// simbolos) para equivalentes simples: o PDF nunca recebe byte invalido.
    /// </summary>
    private static char Map(char character) => character switch
    {
        '\u2018' or '\u2019' => '\'',
        '\u201c' or '\u201d' => '"',
        '\u2013' or '\u2014' => '-',
        '\u2026' => '.',
        '\u00a0' => ' ',
        '\u2022' => '*',
        '\u2192' => '>',
        _ => character is < ' ' or > '\u00ff' ? '?' : character
    };

    private static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void Write(Stream stream, string text)
    {
        var bytes = Encoding.Latin1.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
