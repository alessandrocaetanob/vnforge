using VNForge.AI.Translation;
using VNForge.Core.Text;

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
        var cached = new HashSet<string>(StringComparer.Ordinal);
        var toSend = new List<int>();
        var sentKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++)
        {
            if (cache.TryGet(keys[i], out _))
            {
                hits++;
                cached.Add(keys[i]);
            }
            else if (sentKeys.Add(keys[i])) toSend.Add(i);
        }

        // Linhas repetidas no mesmo lote pagam uma vez só.
        ledger?.RecordCacheHits(hits + (batch.Lines.Count - hits - toSend.Count));

        var (refused, doubts) = toSend.Count > 0
            ? await TranslateMissingAsync(batch, keys, toSend, ct).ConfigureAwait(false)
            : ([], new Dictionary<string, TranslationDoubt>(StringComparer.Ordinal));

        return [.. batch.Lines.Select((u, i) =>
        {
            return cache.TryGet(keys[i], out var translation)
                ? new TranslationResult(u.Id, translation, Doubt: doubts.GetValueOrDefault(keys[i]), FromCache: cached.Contains(keys[i]))
                : new TranslationResult(u.Id, string.Empty, Refused: refused.Contains(keys[i]));
        })];
    }

    /// <summary>
    /// Manda ao provedor só as linhas fora do cache e guarda as traduções. Devolve as chaves das linhas recusadas e as dúvidas que o provedor
    /// registrou, que não vão para o cache: a linha que sai do cache na próxima execução não traz dúvida.
    /// </summary>
    private async Task<(HashSet<string> Refused, Dictionary<string, TranslationDoubt> Doubts)> TranslateMissingAsync(TranslationBatch batch, List<string> keys, List<int> toSend, CancellationToken ct)
    {
        var request = batch.ForLines([.. toSend.Select(i => batch.Lines[i])]);

        var results = await inner.TranslateAsync(request, ct).ConfigureAwait(false);
        var byId = new Dictionary<string, TranslationResult>(StringComparer.Ordinal);
        foreach (var r in results)
            byId[r.Id] = r;

        var refused = new HashSet<string>(StringComparer.Ordinal);
        var doubts = new Dictionary<string, TranslationDoubt>(StringComparer.Ordinal);
        foreach (var i in toSend)
        {
            if (!byId.TryGetValue(batch.Lines[i].Id, out var result))
                throw new InvalidOperationException($"O provedor não devolveu a linha '{batch.Lines[i].Id}'.");

            // Recusa não é tradução: guardá-la no cache faria a próxima execução devolver texto vazio sem tentar de novo.
            if (result.Refused || string.IsNullOrEmpty(result.Translation))
                refused.Add(keys[i]);
            else
            {
                cache.Set(keys[i], result.Translation);
                if (result.Doubt is not null)
                    doubts[keys[i]] = result.Doubt;
            }
        }

        return (refused, doubts);
    }
}
