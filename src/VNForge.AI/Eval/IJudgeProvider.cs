using VNForge.AI.Claude;
using VNForge.AI.Translation;

namespace VNForge.AI.Eval;

/// <summary>A nota do juiz para uma linha, de 0 a 10. <paramref name="Refused"/> é a linha que o modelo se recusou a avaliar; ela fica sem nota.</summary>
public sealed record JudgeScore(string Id, double Score, bool Refused = false);

/// <summary>
/// O juiz de IA do banco de provas: dá uma nota por tradução sem saber qual configuração a gerou. O lote traz só o original (<c>Source</c>) e o texto a avaliar
/// (<c>Translation</c>); nada do que identifica a configuração entra no pedido.
/// </summary>
public interface IJudgeProvider
{
    /// <summary>Tokens acumulados de todas as chamadas deste juiz; nulo quando ele não usa a API.</summary>
    ClaudeUsage? Usage { get; }

    /// <summary>Uma nota por linha do lote, na ordem do lote.</summary>
    Task<IReadOnlyList<JudgeScore>> JudgeAsync(TranslationBatch batch, CancellationToken ct);
}
