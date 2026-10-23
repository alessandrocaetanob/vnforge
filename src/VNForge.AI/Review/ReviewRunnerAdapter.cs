using VNForge.AI.Translation;

namespace VNForge.AI.Review;

/// <summary>
/// Faz um <see cref="IReviewProvider"/> passar por um <see cref="TranslationRunner"/>, para a revisão herdar lotes, paralelismo,
/// retry e cancelamento sem copiar o runner. A sugestão viaja em <see cref="TranslationResult.Translation"/>; quem lê o resultado
/// do runner a trata como sugestão, nunca como tradução.
/// </summary>
public sealed class ReviewRunnerAdapter(IReviewProvider inner) : ITranslationProvider
{
    private readonly IReviewProvider _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
    {
        var results = await _inner.ReviewAsync(batch, ct).ConfigureAwait(false);
        return [.. results.Select(r => new TranslationResult(r.Id, r.Suggestion, r.Refused))];
    }
}
