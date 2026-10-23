using VNForge.AI.Claude;

namespace VNForge.AI.Cost;

/// <summary>Preço em dólares por milhão de tokens.</summary>
public readonly record struct ModelPrice(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite)
{
    public decimal Cost(TokenUsage u) =>
        (u.Input * Input + u.Output * Output + u.CacheRead * CacheRead + u.CacheWrite * CacheWrite) / 1_000_000m;
}

/// <summary>
/// Tabela de preços da API (outubro de 2026, tabela "Current Models" da skill claude-api; o Haiku 5.5 vale até 100 mil
/// tokens de prompt). A escrita de cache usa o multiplicador de 1,25x sobre a entrada (cache de 5 minutos) e a leitura do
/// Haiku usa 0,1x: esses dois valores são derivados, a fonte não os lista. Confira em docs.anthropic.com/pricing antes de
/// tratar o custo como definitivo.
/// </summary>
public static class ModelPricing
{
    private static readonly Dictionary<string, ModelPrice> Table = new()
    {
        [ClaudeModels.Opus] = new(4.00m, 20.00m, 0.20m, 5.00m),
        [ClaudeModels.Sonnet] = new(2.00m, 10.00m, 0.20m, 2.50m),
        [ClaudeModels.Haiku] = new(0.10m, 0.50m, 0.01m, 0.125m),
        [ClaudeModels.Fable] = new(10.00m, 50.00m, 0.25m, 12.50m),
    };

    public static ModelPrice For(string model) =>
        Table.TryGetValue(model, out var price)
            ? price
            : throw new ArgumentException($"Sem preço para o modelo '{model}'.", nameof(model));
}
