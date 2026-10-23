using VNForge.AI.Translation;

namespace VNForge.AI.Cost;

/// <summary>
/// Põe o cache por hash na frente de qualquer <see cref="ITranslationProvider"/>: só as linhas sem tradução em cache vão ao
/// provedor, e as traduções novas entram no cache. Um lote inteiramente em cache não chama o provedor. A persistência
/// em disco (<see cref="TranslationCache.Save"/>) fica com quem orquestra a execução.
/// </summary>
public sealed class CachingTranslationProvider(ITranslationProvider inner, TranslationCache cache, string model, UsageLedger? ledger = null) : ITranslationProvider
{
    public async Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var context = TranslationCache.ContextFor(model, batch);
        var keys = batch.Lines.Select(u => TranslationCache.KeyFor(u, context)).ToList();

        var hits = 0;
        var toSend = new List<int>();
        var sentKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++)
        {
            if (cache.TryGet(keys[i], out _)) hits++;
            else if (sentKeys.Add(keys[i])) toSend.Add(i);
        }

        // Linhas repetidas no mesmo lote pagam uma vez só.
        ledger?.RecordCacheHits(hits + (batch.Lines.Count - hits - toSend.Count));

        if (toSend.Count > 0)
        {
            var request = new TranslationBatch([.. toSend.Select(i => batch.Lines[i])])
            {
                PreviousLines = batch.PreviousLines,
                Glossary = batch.Glossary,
                Characters = batch.Characters,
                StyleGuide = batch.StyleGuide,
            };

            var results = await inner.TranslateAsync(request, ct).ConfigureAwait(false);
            var byId = results.ToDictionary(r => r.Id, r => r.Translation, StringComparer.Ordinal);
            foreach (var i in toSend)
                cache.Set(keys[i], byId[batch.Lines[i].Id]);
        }

        return [.. batch.Lines.Select((u, i) =>
        {
            cache.TryGet(keys[i], out var translation);
            return new TranslationResult(u.Id, translation);
        })];
    }
}
