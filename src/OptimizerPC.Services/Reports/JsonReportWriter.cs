using System.Text.Json;
using OptimizerPC.Services.Configuration;

namespace OptimizerPC.Services.Reports;

/// <summary>
/// Renderizacao em JSON. O documento ja esta em texto final: o arquivo serve para
/// integracao e leitura por outras ferramentas locais.
/// </summary>
internal static class JsonReportWriter
{
    internal static string Write(ReportDocument document)
    {
        var options = AppJson.CreateOptions(indented: true);
        return JsonSerializer.Serialize(document, options);
    }
}
