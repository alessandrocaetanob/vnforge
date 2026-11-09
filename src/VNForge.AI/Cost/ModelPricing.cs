using VNForge.AI.Claude;

namespace VNForge.AI.Cost;

/// <summary>Preço em dólares por milhão de tokens.</summary>
public readonly record struct ModelPrice(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite)
{
    public decimal Cost(TokenUsage u) =>
        (u.Input * Input + u.Output * Output + u.CacheRead * CacheRead + u.CacheWrite * CacheWrite) / 1_000_000m;
}

/// <summary>
/// Tabela de preços da API (platform.claude.com, página de preços, conferida em 09/11/2026). A escrita de cache é a de 5 minutos. A leitura de cache
/// custa 10% da entrada no Haiku 5.5, 5% no Opus 5.5 e no Sonnet 5.5 (US$ 0,20 e US$ 0,10) e 2,5% no Fable 5.1. O Haiku 5.5 tem duas faixas: o prompt
/// de um pedido (entrada, leitura e escrita de cache) acima de <see cref="LongPromptTokens"/> paga a faixa longa, por todos os tokens desse pedido.
/// </summary>
public static class ModelPricing
{
    /// <summary>Acima deste tamanho de prompt por pedido, o Haiku 5.5 cobra a faixa longa.</summary>
    public const long LongPromptTokens = 100_000;

    private static readonly Dictionary<string, ModelPrice> Table = new()
    {
        [ClaudeModels.Opus] = new(4.00m, 20.00m, 0.20m, 5.00m),
        [ClaudeModels.Sonnet] = new(2.00m, 10.00m, 0.10m, 2.50m),
        [ClaudeModels.Haiku] = new(0.10m, 0.50m, 0.01m, 0.125m),
        [ClaudeModels.Fable] = new(10.00m, 50.00m, 0.25m, 12.50m),
    };

    private static readonly Dictionary<string, ModelPrice> LongPromptTable = new()
    {
        [ClaudeModels.Haiku] = new(0.50m, 2.50m, 0.05m, 0.625m),
    };

    /// <summary>O preço da faixa curta, a única dos modelos sem faixa longa.</summary>
    public static ModelPrice For(string model) =>
        Table.TryGetValue(model, out var price)
            ? price
            : throw new ArgumentException($"Sem preço para o modelo '{model}'.", nameof(model));

    /// <summary>O preço para um pedido de <paramref name="promptTokens"/> tokens de prompt: a faixa longa quando o modelo a tem e o prompt passa de <see cref="LongPromptTokens"/>.</summary>
    public static ModelPrice For(string model, long promptTokens) =>
        promptTokens > LongPromptTokens && LongPromptTable.TryGetValue(model, out var price) ? price : For(model);

    /// <summary>O custo de um uso acumulado, cobrando <paramref name="inLongPrompts"/> (a parte vinda de pedidos acima do limite) na faixa longa.</summary>
    public static decimal Cost(string model, TokenUsage total, TokenUsage inLongPrompts)
    {
        var shortPart = new TokenUsage(
            total.Input - inLongPrompts.Input,
            total.Output - inLongPrompts.Output,
            total.CacheRead - inLongPrompts.CacheRead,
            total.CacheWrite - inLongPrompts.CacheWrite);
        return For(model).Cost(shortPart) + For(model, LongPromptTokens + 1).Cost(inLongPrompts);
    }
}
