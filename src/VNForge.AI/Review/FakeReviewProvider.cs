using System.Collections.Concurrent;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Review;

/// <summary>Provedor de revisão determinístico para testes: sem rede e sem SDK. Por padrão sugere <c>Conferir: X</c>, com X a tradução da linha.</summary>
public sealed class FakeReviewProvider : IReviewProvider
{
    private readonly Func<TextUnit, string> _review;
    private readonly ConcurrentQueue<TranslationBatch> _received = new();

    public FakeReviewProvider(Func<TextUnit, string>? review = null)
    {
        _review = review ?? (unit => $"Conferir: {unit.Translation}");
    }

    /// <summary>Lotes recebidos, na ordem das chamadas.</summary>
    public IReadOnlyCollection<TranslationBatch> ReceivedBatches => _received;

    public Task<IReadOnlyList<ReviewResult>> ReviewAsync(TranslationBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<ReviewResult>>(ct);

        _received.Enqueue(batch);
        IReadOnlyList<ReviewResult> results = [.. batch.Lines.Select(u => new ReviewResult(u.Id, _review(u)))];
        return Task.FromResult(results);
    }
}
