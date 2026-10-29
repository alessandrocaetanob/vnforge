using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Search;
using VNForge.Core.Text;

namespace VNForge.AI.BatchRequest;

/// <summary>Como terminou um pedido em lote: as linhas que mudam (a prévia) e o que ficou de fora.</summary>
/// <param name="Items">As linhas reescritas, na ordem do projeto; nada foi gravado.</param>
/// <param name="Unchanged">Linhas que a IA leu e deixou como estão.</param>
/// <param name="Refused">Linhas que a IA recusou ou devolveu com marcadores de controle diferentes dos da tradução; ficam como estão.</param>
/// <param name="Failed">Linhas dos lotes que não voltaram, mesmo depois das tentativas.</param>
/// <param name="Stopped">Por que o limite de gasto parou o pedido; <see langword="null"/> quando não parou.</param>
/// <param name="Problem">A primeira falha, em português comum; <see langword="null"/> quando todos os lotes voltaram.</param>
/// <param name="Canceled">A pessoa cancelou; as linhas já prontas estão em <paramref name="Items"/>.</param>
public sealed record BatchRequestOutcome(IReadOnlyList<ReplaceItem> Items, int Unchanged, int Refused, int Failed, BudgetStop? Stopped, string? Problem, bool Canceled);

/// <summary>
/// Manda as falas candidatas de um pedido em lote à IA, em lotes, e reúne o que ela reescreveu numa lista de <see cref="ReplaceItem"/>, a mesma da
/// prévia de busca e substituição. Não grava nada. Usa o <see cref="TranslationRunner"/>: lotes em paralelo, tentativas, cancelamento e a guarda de gasto.
/// </summary>
public sealed class BatchRequestRunner(IBatchRequestProvider provider, TranslationBatchBuilder builder, TranslationRunOptions options)
{
    /// <summary>
    /// Estima o gasto de pedir a reescrita de <paramref name="lines"/>. O pedido leva o original e a tradução de cada linha, e a resposta só as que
    /// mudam; contar as duas na entrada e a resposta inteira dá um teto, não o valor exato.
    /// </summary>
    public static CostEstimate Estimate(IReadOnlyList<TextUnit> lines, TranslationBatchBuilder builder, string model, ClaudeEffort effort, int batchSize, string request = "")
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(builder);

        // O texto do pedido vai inteiro em cada lote; entra na estimativa como parte da primeira linha de cada um.
        var batches = lines.Chunk(batchSize).Select(chunk => builder.Build([.. chunk.Select((u, i) => u with { Source = $"{(i == 0 ? request + " " : string.Empty)}{u.Source} {u.Translation}" })]));
        return CostEstimator.EstimateBatches(batches, model, null, effort);
    }

    public async Task<BatchRequestOutcome> RunAsync(string request, IReadOnlyList<BatchCandidate> candidates, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        ArgumentNullException.ThrowIfNull(candidates);
        var byId = candidates.ToDictionary(c => c.Unit.Id, StringComparer.Ordinal);
        var changed = new Dictionary<string, ReplaceItem>(StringComparer.Ordinal);
        var unchanged = 0;
        var refused = 0;

        void OnBatch(IReadOnlyList<TranslationResult> results)
        {
            foreach (var result in results)
            {
                var candidate = byId[result.Id];
                if (result.Refused)
                {
                    refused++;
                }
                else if (string.Equals(result.Translation, candidate.Unit.Translation, StringComparison.Ordinal))
                {
                    unchanged++;
                }
                else
                {
                    changed[result.Id] = new ReplaceItem(candidate.ScriptKey, candidate.Number, result.Id, candidate.Unit.Translation!, result.Translation, 0, candidate.Unit.Status);
                }
            }
        }

        var runner = new TranslationRunner(new BatchRequestAdapter(provider, request), options);
        var run = await runner.RunAsync([.. candidates.Select(c => c.Unit)], lines => builder.Build(lines), OnBatch, null, ct).ConfigureAwait(false);

        var items = candidates.Where(c => changed.ContainsKey(c.Unit.Id)).Select(c => changed[c.Unit.Id]).ToList();
        var failed = run.Failures.Sum(f => f.LineIds.Count);
        var problem = run.Failures.Count > 0 ? ClaudeErrorMessages.Describe(run.Failures[0].Error) : null;
        return new BatchRequestOutcome(items, unchanged, refused, failed, run.BudgetStop, problem, run.Canceled);
    }
}

/// <summary>
/// Faz um <see cref="IBatchRequestProvider"/> passar pelo <see cref="TranslationRunner"/>, que espera uma resposta por linha: a linha que a IA não
/// devolveu volta com a tradução de antes. Confere que a reescrita mantém os marcadores de controle da tradução e pede de novo as que não mantêm.
/// </summary>
internal sealed class BatchRequestAdapter(IBatchRequestProvider inner, string request, int maxAttempts = 2) : ITranslationProvider
{
    public async Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var accepted = new Dictionary<string, TranslationResult>(StringComparer.Ordinal);
        var current = batch;
        for (var attempt = 1; attempt <= maxAttempts && current.Lines.Count > 0; attempt++)
        {
            var rewrites = await inner.RewriteAsync(request, current, ct).ConfigureAwait(false);
            var byId = rewrites.GroupBy(r => r.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var proposed = new List<TranslationResult>();
            foreach (var line in current.Lines)
            {
                if (!byId.TryGetValue(line.Id, out var rewrite))
                {
                    accepted[line.Id] = new TranslationResult(line.Id, line.Translation ?? string.Empty);
                }
                else if (rewrite.Refused)
                {
                    accepted[line.Id] = new TranslationResult(line.Id, string.Empty, Refused: true);
                }
                else
                {
                    proposed.Add(new TranslationResult(line.Id, rewrite.Translation));
                }
            }

            // Só as linhas que a IA reescreveu vão à validação: a que ela deixou de fora está sem mudança, não faltando, e pedir de novo seria gasto à toa.
            // Os marcadores se comparam com a tradução de agora, não com o original em japonês: o validador compara com Source.
            var proposedIds = proposed.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            var view = current.ForLines([.. current.Lines.Where(l => proposedIds.Contains(l.Id)).Select(l => l with { Source = l.Translation ?? string.Empty })]);
            var report = TranslationResponseValidator.Validate(view, proposed);
            foreach (var result in report.Valid)
            {
                accepted[result.Id] = result;
            }

            var again = report.Problems.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            current = batch.ForLines([.. current.Lines.Where(l => again.Contains(l.Id))]);
        }

        return [.. batch.Lines.Select(l => accepted.TryGetValue(l.Id, out var r) ? r : new TranslationResult(l.Id, string.Empty, Refused: true))];
    }
}
