namespace OptimizerPC.App.Services;

/// <summary>
/// Ponte entre telas para abrir uma ferramenta especifica do Windows: uma tela
/// registra a solicitacao, a tela de ferramentas consome o pedido uma unica vez.
/// </summary>
public sealed class ToolLaunchState
{
    private string? _pendingToolId;

    public void Request(string? toolId) =>
        _pendingToolId = string.IsNullOrWhiteSpace(toolId) ? null : toolId.Trim();

    public string? Consume()
    {
        var pending = _pendingToolId;
        _pendingToolId = null;
        return pending;
    }
}
