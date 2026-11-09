namespace VNForge.AI.Claude;

/// <summary>Pedido já montado, independente do SDK: é o que os testes inspecionam.</summary>
public sealed record ClaudeRequest(
    string Model,
    ClaudeEffort Effort,
    int MaxTokens,
    string System,
    string User,
    string JsonSchema,
    string BatchContext = "")
{
    /// <summary>O sistema inteiro: <see cref="System"/> (igual a cada lote do projeto) e <see cref="BatchContext"/> (glossário e personagens do lote).</summary>
    public string FullSystem => System + BatchContext;
}

/// <summary>Tokens de uma chamada; <see cref="CacheReadInputTokens"/> são os lidos do cache de prompt (cobrados mais barato).</summary>
/// <param name="InLongPrompts">A parte do total que veio de pedidos com prompt acima de <see cref="Cost.ModelPricing.LongPromptTokens"/>, que o Haiku cobra em outra faixa; nula quando nenhum pedido passou do limite.</param>
public sealed record ClaudeUsage(long InputTokens, long OutputTokens, long CacheReadInputTokens, long CacheCreationInputTokens, ClaudeUsage? InLongPrompts = null)
{
    public static ClaudeUsage operator +(ClaudeUsage a, ClaudeUsage b) => new(
        a.InputTokens + b.InputTokens,
        a.OutputTokens + b.OutputTokens,
        a.CacheReadInputTokens + b.CacheReadInputTokens,
        a.CacheCreationInputTokens + b.CacheCreationInputTokens,
        a.InLongPrompts is null ? b.InLongPrompts : b.InLongPrompts is null ? a.InLongPrompts : a.InLongPrompts + b.InLongPrompts);

    /// <summary>O uso de um único pedido, marcado como de prompt longo quando entrada, leitura e escrita de cache somam mais que o limite da faixa.</summary>
    public static ClaudeUsage ForRequest(long input, long output, long cacheRead, long cacheCreation)
    {
        var usage = new ClaudeUsage(input, output, cacheRead, cacheCreation);
        return input + cacheRead + cacheCreation > Cost.ModelPricing.LongPromptTokens ? usage with { InLongPrompts = usage } : usage;
    }
}

/// <summary>Resposta reduzida ao que o provedor usa: o <c>stop_reason</c>, o texto concatenado e o uso de tokens.</summary>
public sealed record ClaudeReply(string? StopReason, string Text, ClaudeUsage? Usage = null);

/// <summary>Fronteira com o SDK. Os testes a trocam por um cliente falso.</summary>
public interface IClaudeMessageClient
{
    Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct);
}

/// <summary>O modelo recusou o pedido (<c>stop_reason: "refusal"</c>).</summary>
public sealed class ClaudeRefusalException(string message, string? detail = null) : Exception(message)
{
    /// <summary>O detalhe técnico (o <c>stop_reason</c>, por exemplo), para o log; a <see cref="Exception.Message"/> é a frase para a pessoa quando o chamador a dá.</summary>
    public string? Detail { get; } = detail;
}

/// <summary>A resposta não é o JSON esperado, ou não bate com os ids do lote.</summary>
public sealed class ClaudeResponseException(string message, Exception? inner = null, string? detail = null) : Exception(message, inner)
{
    /// <summary>O <see cref="Detail"/> de uma resposta cortada pelo limite de tokens de saída; repetir o pedido com o mesmo limite corta no mesmo ponto.</summary>
    public const string MaxTokensDetail = "stop_reason: max_tokens";

    public bool HitMaxTokens => Detail == MaxTokensDetail;

    /// <summary>O detalhe técnico (o que faltou ou sobrou na resposta), para o log; a <see cref="Exception.Message"/> é a frase para a pessoa quando o chamador a dá.</summary>
    public string? Detail { get; } = detail;
}
