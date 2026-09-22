namespace OptimizerPC.App.Controls;

/// <summary>
/// Interruptor global das animacoes. Reflete a opcao "Reduzir animacoes" das
/// configuracoes, que existe para respeitar preferencias de acessibilidade.
/// </summary>
public static class Motion
{
    public static bool Enabled { get; set; } = true;
}
