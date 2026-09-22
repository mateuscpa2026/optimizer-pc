namespace OptimizerPC.Services.Reports;

/// <summary>
/// Conteudo de um relatorio ja traduzido e pronto para ser renderizado. Os
/// renderizadores recebem apenas texto final: nenhuma consulta ao sistema acontece
/// na etapa de escrita do arquivo.
/// </summary>
internal sealed class ReportDocument
{
    public string Title { get; init; } = string.Empty;

    public DateTime GeneratedAtUtc { get; init; }

    public string GeneratedAtLabel { get; init; } = string.Empty;

    /// <summary>Idioma em que o documento foi gerado, usado no atributo lang do HTML.</summary>
    public string LanguageCode { get; init; } = "pt-BR";

    public string Introduction { get; init; } = string.Empty;

    public string Footer { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<ReportSection> Sections { get; init; } = Array.Empty<ReportSection>();

    /// <summary>Nome de arquivo sugerido, derivado do horario local da geracao.</summary>
    internal string BuildFileName(string extension)
    {
        var stamp = GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd-HHmmss");
        return "OptimizerPC-Relatorio-" + stamp + extension;
    }
}

/// <summary>Bloco do relatorio: campos simples e tabelas.</summary>
internal sealed class ReportSection
{
    public string Title { get; init; } = string.Empty;

    public string? Note { get; init; }

    public IReadOnlyList<ReportField> Fields { get; init; } = Array.Empty<ReportField>();

    public IReadOnlyList<ReportTable> Tables { get; init; } = Array.Empty<ReportTable>();

    internal bool IsEmpty => Fields.Count == 0 && Tables.Count == 0;
}

internal sealed class ReportField
{
    public string Label { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;
}

internal sealed class ReportTable
{
    public string? Title { get; init; }

    public IReadOnlyList<string> Headers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Array.Empty<IReadOnlyList<string>>();

    /// <summary>Linha exibida quando a tabela nao tem dados. Nunca deixamos a tabela vazia.</summary>
    public string EmptyText { get; init; } = string.Empty;
}
