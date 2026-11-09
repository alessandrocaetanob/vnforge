using System.Text;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Batches;

/// <summary>O que a pessoa pediu para a noite. O app e o <c>vnf translate --batch</c> montam o mesmo pedido.</summary>
/// <param name="Title">O escopo em português ("O capítulo"); aparece na lista de tarefas.</param>
/// <param name="Model">O modelo (a qualidade escolhida).</param>
/// <param name="Rate">A cotação que converte o preço em reais.</param>
/// <param name="ScriptFilter">Quais scripts entram; <see langword="null"/> aceita todos.</param>
/// <param name="Wanted">Quais linhas de um script entram; o padrão são as sem tradução e com original.</param>
/// <param name="Limit">Máximo de linhas; <see langword="null"/> sem limite.</param>
public sealed record NightBatchOrder(
    string Title,
    string Model,
    ClaudeEffort Effort,
    int BatchSize,
    ExchangeRate Rate,
    Func<string, bool>? ScriptFilter = null,
    Func<TextUnit, bool>? Wanted = null,
    int? Limit = null);

/// <summary>Uma consulta ao lote: o estado guardado, o que a API disse e, quando o lote terminou, se o resultado já foi gravado agora.</summary>
/// <param name="Info">O que a API informou; <see langword="null"/> quando o lote já estava gravado e nada foi perguntado.</param>
/// <param name="JustApplied">O resultado foi gravado nesta consulta.</param>
public sealed record NightBatchCheck(NightBatchState State, MessageBatchInfo? Info, bool JustApplied);

/// <summary>Erro esperado do lote noturno, com a mensagem pronta para a pessoa.</summary>
public sealed class NightBatchException(string message, Exception? inner = null) : VNForgeException(message, inner);

/// <summary>
/// A tradução noturna: manda as linhas pendentes de um escopo pela Message Batches API (metade do preço), guarda o id do lote em
/// <c>.cache/lote-noturno/</c> e, quando o lote termina, grava o resultado em <c>text/*.json</c> com um ponto de restauração. Cada passo
/// (<see cref="SubmitAsync"/>, <see cref="CheckAsync"/>, <see cref="CancelAsync"/>) lê o estado do disco, então o app e o <c>vnf</c> retomam um
/// lote que o outro enviou, depois de fechados.
/// </summary>
/// <remarks>
/// No lote a fala anterior ainda não está traduzida: o contexto vem do original (<see cref="TranslationBatchBuilder.PreviousFromSource"/>) e do resumo da cena da bíblia.
/// O resultado pode chegar horas depois, então só se grava a linha que continua como estava no envio (<see cref="NightBatchLine.Matches"/>).
/// </remarks>
public sealed class NightBatchService(IMessageBatchClient client, ProjectFolder project, TimeProvider? clock = null)
{
    /// <summary>De quanto em quanto tempo o app pergunta à API se o lote terminou. O lote leva até 24 h; perguntar mais seguido não o apressa.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

    private const string CacheFileName = "translations.json";
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly NightBatchStore store = new(project);

    /// <summary>Os lotes do projeto que ainda não foram gravados.</summary>
    public IReadOnlyList<NightBatchState> Open() => store.Open();

    public IReadOnlyList<NightBatchState> All() => store.List();

    /// <summary>
    /// O que o pedido custaria: o preço do pedido normal pela metade, em reais. Não chama a API.
    /// </summary>
    public static decimal PriceBrl(decimal normalBrl) => normalBrl * ModelPricing.BatchDiscount;

    /// <summary>
    /// Monta os pedidos das linhas pendentes, envia o lote e guarda o estado. Devolve <see langword="null"/> quando não há linha a traduzir.
    /// </summary>
    /// <exception cref="NightBatchException">O escopo passa do limite de um lote.</exception>
    public async Task<NightBatchState?> SubmitAsync(NightBatchOrder order, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        var selection = LineSelection.Build(project.LoadAllText(), order.ScriptFilter, order.Limit, order.Wanted ?? TranslationQuoter.IsPending);
        var pending = selection.Pending;
        if (pending.Count == 0)
        {
            return null;
        }

        var config = project.LoadConfig();
        var builder = BuilderFor(config);
        var options = new ClaudeOptions { Model = order.Model, Effort = order.Effort };
        var batchSize = Math.Clamp(order.BatchSize, TranslationRunOptions.MinBatchSize, TranslationRunOptions.MaxBatchSize);
        var batches = pending.Chunk(batchSize).Select(chunk => selection.BuildBatch(builder, chunk)).ToList();
        var items = batches.Select((batch, i) => new MessageBatchItem(CustomId(i), ClaudeTranslationProvider.BuildRequest(batch, options))).ToList();
        CheckLimits(items);

        var cache = TranslationCache.Load(Path.Combine(project.CacheDirectory, CacheFileName));
        var estimate = CostEstimator.EstimateBatches(batches, order.Model, cache, order.Effort);
        var info = await client.CreateAsync(items, ct).ConfigureAwait(false);

        var requests = batches.Select((batch, i) => new NightBatchRequest(CustomId(i), [.. batch.Lines.Select(NightBatchLine.Of)])).ToList();
        var state = new NightBatchState(
            info.Id,
            order.Title,
            order.Model,
            (QualityProfiles.ForModel(order.Model)?.Quality)?.ToString() ?? order.Model,
            order.Effort.ToString(),
            clock.GetUtcNow(),
            PriceBrl(order.Rate.ToBrl(estimate.Dollars)),
            [.. requests.SelectMany(r => r.Lines).Select(l => ScriptKeyOf(l.Id)).Distinct(StringComparer.Ordinal)],
            requests);
        store.Save(state);
        return state;
    }

    /// <summary>
    /// Pergunta à API pelo lote. Se ele terminou, grava o resultado (uma vez só) e marca o estado como gravado; se ainda roda, só devolve o andamento.
    /// </summary>
    /// <exception cref="NightBatchException">O lote não está no projeto.</exception>
    public async Task<NightBatchCheck> CheckAsync(string batchId, CancellationToken ct)
    {
        var state = store.Find(batchId) ?? throw new NightBatchException($"O lote {batchId} não está neste projeto.");
        if (!state.IsOpen)
        {
            return new NightBatchCheck(state, null, false);
        }

        var info = await client.GetAsync(batchId, ct).ConfigureAwait(false);
        if (info.Status != MessageBatchStatus.Ended)
        {
            if (info.Status == MessageBatchStatus.Canceling && state.Phase != NightBatchPhase.Canceling)
            {
                state = state with { Phase = NightBatchPhase.Canceling };
                store.Save(state);
            }

            return new NightBatchCheck(state, info, false);
        }

        var applied = await ApplyAsync(state, ct).ConfigureAwait(false);
        return new NightBatchCheck(applied, info, true);
    }

    /// <summary>Pede à API o cancelamento. O que o lote já tinha pronto ainda chega e é gravado quando ele encerra.</summary>
    public async Task<NightBatchState> CancelAsync(string batchId, CancellationToken ct)
    {
        var state = store.Find(batchId) ?? throw new NightBatchException($"O lote {batchId} não está neste projeto.");
        if (!state.IsOpen)
        {
            return state;
        }

        await client.CancelAsync(batchId, ct).ConfigureAwait(false);
        state = state with { Phase = NightBatchPhase.Canceling };
        store.Save(state);
        return state;
    }

    private TranslationBatchBuilder BuilderFor(ProjectConfig config) =>
        new(
            project.LoadGlossary(),
            new CharacterBook(project.LoadCharacters()),
            TranslationBatchBuilder.ReadStyleGuide(Path.Combine(project.Root, TranslationContextSummary.StyleGuideFileName)),
            style: config.Style,
            maxPreviousLines: config.Ai.ContextBefore,
            maxNextLines: config.Ai.ContextAfter,
            addressTerms: project.LoadAddressTerms())
        {
            Bible = project.LoadBibleOrEmpty(out _),
            PreviousFromSource = true,
        };

    private async Task<NightBatchState> ApplyAsync(NightBatchState state, CancellationToken ct)
    {
        var entries = new Dictionary<string, MessageBatchEntry>(StringComparer.Ordinal);
        await foreach (var entry in client.ResultsAsync(state.Id, ct).ConfigureAwait(false))
        {
            entries[entry.CustomId] = entry;
        }

        var config = project.LoadConfig();
        var expected = state.Requests.SelectMany(r => r.Lines).ToDictionary(l => l.Id, StringComparer.Ordinal);
        var texts = project.LoadAllText();
        var scriptKeys = state.ScriptKeys.ToHashSet(StringComparer.Ordinal);

        // Só a linha que continua como no envio entra na seleção; a que a pessoa mexeu nas horas do lote fica como ela deixou.
        var selection = LineSelection.Build(texts, scriptKeys.Contains, null, unit => expected.TryGetValue(unit.Id, out var line) && line.Matches(unit));
        var selected = selection.Pending.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var currentById = texts.Values.SelectMany(units => units).ToDictionary(u => u.Id, StringComparer.Ordinal);

        var results = new List<TranslationResult>();
        var failed = 0;
        var refused = 0;
        var unfinished = new List<string>();
        var usage = new ClaudeUsage(0, 0, 0, 0);
        var hasUsage = false;
        foreach (var request in state.Requests)
        {
            var batch = new TranslationBatch([.. request.Lines.Select(l => currentById.TryGetValue(l.Id, out var unit) ? unit : Stub(l.Id))]);
            var outcome = await ReadRequestAsync(request, batch, entries, config, ct).ConfigureAwait(false);
            if (outcome.Usage is { } used)
            {
                usage += used;
                hasUsage = true;
            }

            foreach (var line in request.Lines.Where(l => selected.Contains(l.Id)))
            {
                if (outcome.Results is { } read && read.FirstOrDefault(r => r.Id == line.Id) is { } result)
                {
                    if (result.Refused || string.IsNullOrWhiteSpace(result.Translation))
                    {
                        refused++;
                        unfinished.Add(line.Id);
                    }
                    else
                    {
                        results.Add(result);
                    }
                }
                else
                {
                    failed++;
                    unfinished.Add(line.Id);
                }
            }
        }

        var skipped = state.LineCount - selected.Count;
        var origin = new TranslationOrigin(MachineTranslation.ProviderName, state.Model, clock.GetUtcNow());
        var recorder = RestoreScope.TryBegin(project, $"Tradução noturna · {state.Title}", state.ScriptKeys, RestorePointStore.IsAiWrite, clock);
        var written = 0;
        if (results.Count > 0)
        {
            var saved = selection.ApplyAndSave(project, results, (result, unit) =>
            {
                written++;
                return MachineTranslation.Apply(origin, result, unit);
            });
            skipped += saved.Skipped;
        }

        var point = RestoreScope.TryComplete(recorder);
        RunCostTracker.Record(project, state.Model, hasUsage ? usage : null, ModelPricing.BatchDiscount);
        var dollars = (RunCostTracker.DollarsFor(state.Model, hasUsage ? usage : null) ?? 0m) * ModelPricing.BatchDiscount;

        var applied = state with
        {
            Phase = NightBatchPhase.Applied,
            EndedAt = clock.GetUtcNow(),
            Outcome = new NightBatchOutcome(written, failed, refused, skipped, point?.Id, point?.LineCount ?? 0, dollars, unfinished),
        };
        store.Save(applied);
        return applied;
    }

    private sealed record RequestRead(IReadOnlyList<TranslationResult>? Results, ClaudeUsage? Usage);

    /// <summary>
    /// As traduções de um pedido, pelas mesmas regras da chamada de hoje: a resposta tem que trazer os ids do lote e passa pela validação e pela troca de pontuação.
    /// Pedido com erro, expirado, cancelado, cortado por limite de tokens ou com resposta ilegível dá <see langword="null"/>: as linhas dele "voltaram com erro".
    /// </summary>
    private static async Task<RequestRead> ReadRequestAsync(
        NightBatchRequest request, TranslationBatch batch, Dictionary<string, MessageBatchEntry> entries, ProjectConfig config, CancellationToken ct)
    {
        if (!entries.TryGetValue(request.CustomId, out var entry) || entry.Outcome != MessageBatchOutcome.Succeeded || entry.Reply is not { } reply)
        {
            return new RequestRead(null, null);
        }

        if (reply.StopReason == "refusal")
        {
            return new RequestRead([.. batch.Lines.Select(l => new TranslationResult(l.Id, string.Empty, Refused: true))], reply.Usage);
        }

        if (reply.StopReason == "max_tokens")
        {
            return new RequestRead(null, reply.Usage);
        }

        IReadOnlyList<TranslationResult> parsed;
        try
        {
            parsed = ClaudeTranslationProvider.ParseReply(reply.Text, batch);
        }
        catch (ClaudeResponseException)
        {
            return new RequestRead(null, reply.Usage);
        }

        var chain = KeyboardPunctuationProvider.Wrap(new ValidatingTranslationProvider(new ReplayProvider(parsed), maxAttempts: 1), config);
        return new RequestRead(await chain.TranslateAsync(batch, ct).ConfigureAwait(false), reply.Usage);
    }

    private sealed class ReplayProvider(IReadOnlyList<TranslationResult> results) : ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct) => Task.FromResult(results);
    }

    private static TextUnit Stub(string id) => new(id, TextUnitKind.Dialogue, null, string.Empty, null, TranslationStatus.Untranslated);

    private static string CustomId(int index) => $"r{index + 1:D5}";

    private static string ScriptKeyOf(string unitId) => unitId.Contains(':', StringComparison.Ordinal) ? unitId[..unitId.IndexOf(':', StringComparison.Ordinal)] : unitId;

    private static void CheckLimits(IReadOnlyList<MessageBatchItem> items)
    {
        if (items.Count > AnthropicMessageBatchClient.MaxRequests)
        {
            throw new NightBatchException($"O escopo tem {items.Count} pedidos e um lote aceita até {AnthropicMessageBatchClient.MaxRequests}. Escolha o capítulo em vez do jogo todo.");
        }

        var bytes = items.Sum(i => (long)Encoding.UTF8.GetByteCount(i.Request.FullSystem + i.Request.User + i.Request.JsonSchema));
        if (bytes > AnthropicMessageBatchClient.MaxBytes)
        {
            throw new NightBatchException("O escopo passa de 256 MB, o tamanho máximo de um lote. Escolha o capítulo em vez do jogo todo.");
        }
    }
}
