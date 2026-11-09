using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Eval;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Eval;

/// <summary>O provedor de tradução de uma configuração e como ler os tokens que ele gastou (acumulados desde que foi criado).</summary>
public sealed record EvalProvider(ITranslationProvider Translator, Func<ClaudeUsage?> Usage);

/// <summary>Onde a rodada está: a configuração em andamento (de 1 a <paramref name="ConfigCount"/>) e a fase.</summary>
public sealed record EvalProgress(int ConfigNumber, int ConfigCount, string ConfigId, EvalPhase Phase);

public enum EvalPhase
{
    Translating,
    Judging,
}

/// <summary>O que uma rodada precisa para rodar. Os provedores entram por fábrica: os testes e o app sem chave trocam o Claude por um falso.</summary>
public sealed record EvalRunRequest(
    ProjectFolder Project,
    IReadOnlyList<EvalConfig> Configs,
    int SampleSize,
    int Seed,
    Func<EvalConfig, EvalProvider> Providers)
{
    /// <summary>O juiz de IA; nulo roda sem nota.</summary>
    public IJudgeProvider? Judge { get; init; }

    /// <summary>O modelo do juiz, para o preço; obrigatório com <see cref="Judge"/>.</summary>
    public string? JudgeModel { get; init; }

    /// <summary>Quantos pares a rodada às cegas ganha.</summary>
    public int PairCount { get; init; } = DefaultPairCount;

    public const int DefaultPairCount = 12;

    /// <summary>Lote, paralelismo e repetição das chamadas de tradução e do juiz.</summary>
    public TranslationRunOptions RunOptions { get; init; } = new();

    /// <summary>
    /// Junta a bíblia do jogo (o resumo da cena) ao lote das configurações que pedem <see cref="EvalConfig.WithBible"/>. Nulo quando o projeto ainda não tem bíblia:
    /// uma configuração com bíblia é então recusada antes de qualquer gasto, em vez de rodar sem ela e enganar a comparação.
    /// </summary>
    public Func<TranslationBatch, TranslationBatch>? ApplyBible { get; init; }

    /// <summary>O limite de gasto do projeto, em reais. A rodada para no meio, sem começar lote novo, quando o gasto (o que o projeto já gastou mais o desta rodada) chega nele.</summary>
    public BudgetLimits Limits { get; init; } = BudgetLimits.None;

    /// <summary>A cotação que converte o preço da tabela em reais para o <see cref="Limits"/>.</summary>
    public ExchangeRate Rate { get; init; } = ExchangeRate.Placeholder;

    public TimeProvider Clock { get; init; } = TimeProvider.System;

    public IProgress<EvalProgress>? Progress { get; init; }
}

/// <summary>
/// Roda o banco de provas: cada configuração traduz as mesmas linhas aprovadas do projeto, o juiz dá uma nota por texto sem saber a configuração, e o chrF compara
/// cada tradução com a aprovada. Nunca grava tradução no projeto: só a rodada em <c>.cache/eval/</c> e o gasto em <c>.cache/ai-usage.json</c>, que o Painel soma.
/// </summary>
public static class EvalRunner
{
    /// <exception cref="EvalException">Configuração inválida, projeto sem linhas aprovadas ou bíblia pedida sem bíblia.</exception>
    public static async Task<EvalRound> RunAsync(EvalRunRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request.Configs, request.ApplyBible is not null);
        if (request.Judge is not null && request.JudgeModel is null)
        {
            throw new EvalException("O juiz de IA precisa de um modelo para o preço.");
        }

        var set = EvalSampleSet.Load(request.Project, request.SampleSize, request.Seed, request.RunOptions.BatchSize);
        var reference = set.Sample.ToDictionary(g => g.Id, g => g.Reference, StringComparer.Ordinal);
        var spend = new Spend(request.Rate.ToBrl(AiUsageLog.Load(request.Project).Dollars));
        var reports = new List<EvalConfigReport>();
        for (var i = 0; i < request.Configs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            reports.Add(await RunConfigAsync(request, set, reference, request.Configs[i], i, spend, ct).ConfigureAwait(false));
        }

        var at = request.Clock.GetUtcNow();
        var round = new EvalRound(
            EvalRound.CurrentVersion,
            EvalStore.NewRoundId(at),
            at,
            request.Seed,
            set.GoldLines,
            set.RemainingLines,
            request.Judge is null ? null : request.JudgeModel,
            set.ToSamples(),
            reports,
            BlindPairs.Generate(reports, request.PairCount, request.Seed),
            []);
        EvalStore.SaveRound(request.Project, round);
        EvalStore.SaveConfigs(request.Project, request.Configs);
        return round;
    }

    /// <summary>Confere as configurações antes de gastar: letras únicas, modelo com preço, esforço conhecido e bíblia só quando há como aplicá-la.</summary>
    internal static void Validate(IReadOnlyList<EvalConfig> configs, bool hasBible)
    {
        ArgumentNullException.ThrowIfNull(configs);
        if (configs.Count == 0)
        {
            throw new EvalException("Escolha ao menos uma configuração para a rodada.");
        }

        if (configs.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != configs.Count)
        {
            throw new EvalException("Duas configurações têm a mesma letra.");
        }

        foreach (var config in configs)
        {
            if (!ClaudeModels.All.Contains(config.Model))
            {
                throw new EvalException($"Configuração {config.Id}: modelo desconhecido '{config.Model}'. Use um de: {string.Join(", ", ClaudeModels.All)}.");
            }

            if (!ClaudeEffortText.TryParse(config.Effort, out _))
            {
                throw new EvalException($"Configuração {config.Id}: esforço desconhecido '{config.Effort}'. Use low, medium ou high.");
            }

            if (config.ContextLines is < 0 or > EvalConfigParser.MaxContextLines)
            {
                throw new EvalException($"Configuração {config.Id}: o contexto vai de 0 a {EvalConfigParser.MaxContextLines} falas.");
            }

            if (config.WithBible && !hasBible)
            {
                throw new EvalException($"A configuração {config.Id} pede a bíblia do jogo, e este projeto ainda não tem bíblia. Gere a bíblia ou troque para sem-biblia.");
            }
        }
    }

    private static async Task<EvalConfigReport> RunConfigAsync(
        EvalRunRequest request, EvalSampleSet set, Dictionary<string, string> reference, EvalConfig config, int index, Spend spend, CancellationToken ct)
    {
        void Report(EvalPhase phase) => request.Progress?.Report(new EvalProgress(index + 1, request.Configs.Count, config.Id, phase));

        var provider = request.Providers(config);
        var effort = ClaudeEffortText.ParseOrDefault(config.Effort);
        var translations = new Dictionary<string, TranslationResult>(StringComparer.Ordinal);
        TranslationBatch BuildBatch(IReadOnlyList<TextUnit> lines)
        {
            var batch = set.BuildBatch(config, lines);
            return config.WithBible ? request.ApplyBible!(batch) : batch;
        }

        decimal Spent() => spend.Total + request.Rate.ToBrl(RunCostTracker.DollarsFor(config.Model, provider.Usage()) ?? 0m);

        // O mesmo guarda do app: não começa lote que passaria do limite e, com os lotes em andamento reservados, o limite vale mesmo com chamadas em paralelo.
        var guard = new BudgetGuard(
            new BudgetLimits(request.Limits.Limit, null),
            Spent,
            lines => request.Rate.ToBrl(CostEstimator.EstimateBatch(BuildBatch(lines), config.Model, effort).Dollars));
        var runner = new TranslationRunner(new ValidatingTranslationProvider(provider.Translator), request.RunOptions with { Budget = guard });
        var gate = new Lock();
        var failures = new List<TranslationBatchFailure>();
        Exception? callbackError = null;
        BudgetStop? stop = null;

        Report(EvalPhase.Translating);
        var started = request.Clock.GetTimestamp();
        try
        {
            await Parallel.ForEachAsync(
                set.Blocks,
                new ParallelOptions { MaxDegreeOfParallelism = request.RunOptions.MaxConcurrency, CancellationToken = ct },
                async (block, token) =>
                {
                    var run = await runner.RunAsync(
                        block,
                        BuildBatch,
                        results =>
                        {
                            lock (gate)
                            {
                                foreach (var result in results)
                                {
                                    translations[result.Id] = result;
                                }
                            }
                        },
                        null,
                        token).ConfigureAwait(false);
                    lock (gate)
                    {
                        failures.AddRange(run.Failures);
                        callbackError ??= run.CallbackError;
                        stop ??= run.BudgetStop;
                    }
                }).ConfigureAwait(false);
        }
        finally
        {
            RunCostTracker.Record(request.Project, config.Model, provider.Usage());
        }

        var seconds = request.Clock.GetElapsedTime(started).TotalSeconds;
        ct.ThrowIfCancellationRequested();
        var configDollars = RunCostTracker.DollarsFor(config.Model, provider.Usage()) ?? 0m;
        spend.Add(request.Rate.ToBrl(configDollars));
        if (stop is not null || (request.Limits.Limit is { } cap && spend.Total >= cap))
        {
            throw new EvalException(StoppedText(request, config));
        }

        var error = callbackError?.Message ?? failures.FirstOrDefault()?.Error.Message;
        var judged = new Dictionary<string, double>(StringComparer.Ordinal);
        decimal judgeDollars = 0m;
        if (request.Judge is { } judge)
        {
            Report(EvalPhase.Judging);
            var (scores, dollars, judgeError) = await JudgeAsync(request, judge, set, translations, ct).ConfigureAwait(false);
            judged = scores;
            judgeDollars = dollars;
            spend.Add(request.Rate.ToBrl(dollars));
            error ??= judgeError;
        }

        var lines = set.Lines
            .Select(unit =>
            {
                var result = translations.GetValueOrDefault(unit.Id);
                var refused = result is null || result.Refused || string.IsNullOrWhiteSpace(result.Translation);
                var text = refused ? string.Empty : result!.Translation;
                return new EvalLineResult(unit.Id, text, refused ? 0.0 : Chrf.Score(reference[unit.Id], text), judged.TryGetValue(unit.Id, out var score) ? score : null, refused);
            })
            .ToList();

        var usage = provider.Usage();
        return new EvalConfigReport(
            config,
            lines,
            usage?.InputTokens ?? 0,
            usage?.OutputTokens ?? 0,
            usage?.CacheReadInputTokens ?? 0,
            usage?.CacheCreationInputTokens ?? 0,
            configDollars,
            judgeDollars,
            seconds,
            error,
            Chrf.Corpus(lines.Select(l => ((string?)reference[l.LineId], (string?)l.Translation))));
    }

    private static string StoppedText(EvalRunRequest request, EvalConfig config) =>
        $"A rodada parou na configuração {config.Id} porque o gasto do projeto chegou ao limite de {EvalText.Brl(request.Limits.Limit ?? 0m)}. O que já foi gasto ficou no gasto do projeto, mas a rodada não foi gravada; aumente o limite nos Ajustes do app ou rode menos configurações.";

    private static async Task<(Dictionary<string, double> Scores, decimal Dollars, string? Error)> JudgeAsync(
        EvalRunRequest request, IJudgeProvider judge, EvalSampleSet set, Dictionary<string, TranslationResult> translations, CancellationToken ct)
    {
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        var toJudge = set.Lines
            .Where(u => translations.TryGetValue(u.Id, out var t) && !t.Refused && !string.IsNullOrWhiteSpace(t.Translation))
            .Select(u => u with { Translation = translations[u.Id].Translation })
            .ToList();
        var before = judge.Usage;
        string? error = null;
        try
        {
            foreach (var chunk in toJudge.Chunk(request.RunOptions.BatchSize))
            {
                var results = await judge.JudgeAsync(set.BuildJudgeBatch(chunk), ct).ConfigureAwait(false);
                foreach (var result in results.Where(r => !r.Refused))
                {
                    scores[result.Id] = result.Score;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = $"O juiz de IA falhou: {ex.Message}";
        }
        finally
        {
            // Cancelar no meio do juiz não pode perder o que ele já gastou.
            RunCostTracker.Record(request.Project, request.JudgeModel!, Delta(before, judge.Usage));
        }

        return (scores, RunCostTracker.DollarsFor(request.JudgeModel!, Delta(before, judge.Usage)) ?? 0m, error);
    }

    private static ClaudeUsage? Delta(ClaudeUsage? before, ClaudeUsage? after) => (before, after) switch
    {
        (_, null) => null,
        (null, var b) => b,
        var (a, b) => new ClaudeUsage(b.InputTokens - a.InputTokens, b.OutputTokens - a.OutputTokens, b.CacheReadInputTokens - a.CacheReadInputTokens, b.CacheCreationInputTokens - a.CacheCreationInputTokens),
    };

    /// <summary>O gasto da rodada em reais: o que o projeto já tinha gasto ao começar mais o das configurações e do juiz que já acabaram.</summary>
    private sealed class Spend(decimal baseline)
    {
        private readonly Lock _gate = new();
        private decimal _finished;

        public decimal Total
        {
            get { lock (_gate) return baseline + _finished; }
        }

        public void Add(decimal brl)
        {
            lock (_gate)
                _finished += brl;
        }
    }
}
