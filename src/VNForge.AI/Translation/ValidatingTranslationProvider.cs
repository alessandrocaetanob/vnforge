namespace VNForge.AI.Translation;

/// <summary>
/// Envolve um provedor, valida cada resposta com <see cref="TranslationResponseValidator"/> e reenvia só as linhas com
/// problema (mesmo contexto do lote, só as linhas ruins). Depois de <c>maxAttempts</c> chamadas, a linha que continua
/// ruim volta como <see cref="TranslationResult.Refused"/>, sem tradução, para a revisão tratar.
/// </summary>
public sealed class ValidatingTranslationProvider : ITranslationProvider
{
    private readonly ITranslationProvider _inner;
    private readonly int _maxAttempts;

    public ValidatingTranslationProvider(ITranslationProvider inner, int maxAttempts = 3)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        _inner = inner;
        _maxAttempts = maxAttempts;
    }

    public async Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var accepted = new Dictionary<string, TranslationResult>(StringComparer.Ordinal);
        var current = batch;
        for (var attempt = 1; attempt <= _maxAttempts && current.Lines.Count > 0; attempt++)
        {
            var response = await _inner.TranslateAsync(current, ct).ConfigureAwait(false);
            var report = TranslationResponseValidator.Validate(current, response);
            foreach (var result in report.Valid)
                accepted[result.Id] = result;

            var pending = report.Problems.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            current = new TranslationBatch([.. current.Lines.Where(l => pending.Contains(l.Id))])
            {
                PreviousLines = batch.PreviousLines,
                Glossary = batch.Glossary,
                Characters = batch.Characters,
                StyleGuide = batch.StyleGuide,
                Shorten = batch.Shorten,
            };
        }

        return [.. batch.Lines.Select(l => accepted.TryGetValue(l.Id, out var r) ? r : new TranslationResult(l.Id, string.Empty, Refused: true))];
    }
}
