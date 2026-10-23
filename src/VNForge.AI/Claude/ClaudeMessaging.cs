namespace VNForge.AI.Claude;

/// <summary>Pedido já montado, independente do SDK: é o que os testes inspecionam.</summary>
public sealed record ClaudeRequest(
    string Model,
    ClaudeEffort Effort,
    int MaxTokens,
    string System,
    string User,
    string JsonSchema);

/// <summary>Resposta reduzida ao que o provedor usa: o <c>stop_reason</c> e o texto concatenado.</summary>
public sealed record ClaudeReply(string? StopReason, string Text);

/// <summary>Fronteira com o SDK. Os testes a trocam por um cliente falso.</summary>
public interface IClaudeMessageClient
{
    Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct);
}

/// <summary>O modelo recusou o pedido (<c>stop_reason: "refusal"</c>).</summary>
public sealed class ClaudeRefusalException(string message) : Exception(message);

/// <summary>A resposta não é o JSON esperado, ou não bate com os ids do lote.</summary>
public sealed class ClaudeResponseException(string message, Exception? inner = null) : Exception(message, inner);
