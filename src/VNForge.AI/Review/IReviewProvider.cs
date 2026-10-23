using VNForge.AI.Translation;

namespace VNForge.AI.Review;

/// <summary>
/// Revisa um lote de linhas já traduzidas. É a fronteira entre o <c>vnf review</c> e a IA: o provedor Claude e o
/// <see cref="FakeReviewProvider"/> dos testes a implementam. O lote é um <see cref="TranslationBatch"/> cujas linhas
/// trazem <c>Translation</c>; o provedor só sugere, nunca devolve uma tradução nova.
/// </summary>
public interface IReviewProvider
{
    /// <summary>Devolve um <see cref="ReviewResult"/> por linha do lote, na ordem do lote.</summary>
    Task<IReadOnlyList<ReviewResult>> ReviewAsync(TranslationBatch batch, CancellationToken ct);
}

/// <summary>A revisão de uma linha.</summary>
/// <param name="Id">Id da linha revisada.</param>
/// <param name="Suggestion">Sugestão em uma ou duas frases; vazia quando a tradução está boa.</param>
/// <param name="Refused">O modelo recusou revisar esta linha; <paramref name="Suggestion"/> fica vazia.</param>
public sealed record ReviewResult(string Id, string Suggestion, bool Refused = false);
