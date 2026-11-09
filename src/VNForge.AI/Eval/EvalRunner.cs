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

        var set = EvalSampleSet.Load(request.Project, request.SampleSize, request.Seed);
        var reference = set.Sample.ToDictionary(g => g.Id, g => g.Reference, StringComparer.Ordinal);
        var reports = new List<EvalConfigReport>();
        for (var i = 0; i < request.Configs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            reports.Add(await RunConfigAsync(request, set, reference, request.Configs[i], i, ct).ConfigureAwait(false));
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
        EvalRunRequest request, EvalSampleSet set, Dictionary<string, string> reference, EvalConfig config, int index, CancellationToken ct)
    {
        void Report(EvalPhase phase) => request.Progress?.Report(new EvalProgress(index + 1, request.Configs.Count, config.Id, phase));

        var provider = request.Providers(config);
        var translations = new Dictionary<string, TranslationResult>(StringComparer.Ordinal);
        var options = request.RunOptions with { Budget = null };
        var runner = new TranslationRunner(new ValidatingTranslationProvider(provider.Translator), options);
        TranslationBatch BuildBatch(IReadOnlyList<TextUnit> lines)
        {
            var batch = set.BuildBatch(config, lines);
            return config.WithBible ? request.ApplyBible!(batch) : batch;
        }

        Report(EvalPhase.Translating);
        var started = request.Clock.GetTimestamp();
        TranslationRunResult run;
        try
        {
            run = await runner.RunAsync(
                set.Lines,
                BuildBatch,
                results =>
                {
                    foreach (var result in results)
                    {
                        translations[result.Id] = result;
                    }
                },
                null,
                ct).ConfigureAwait(false);
        }
        finally
        {
            RunCostTracker.Record(request.Project, config.Model, provider.Usage());
        }

        var seconds = request.Clock.GetElapsedTime(started).TotalSeconds;
        ct.ThrowIfCancellationRequested();
        var error = run.CallbackError?.Message ?? run.Failures.FirstOrDefault()?.Error.Message;

        var judged = new Dictionary<string, double>(StringComparer.Ordinal);
        decimal judgeDollars = 0m;
        if (request.Judge is { } judge)
        {
            Report(EvalPhase.Judging);
            var (scores, dollars, judgeError) = await JudgeAsync(request, judge, set, translations, ct).ConfigureAwait(false);
            judged = scores;
            judgeDollars = dollars;
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
            RunCostTracker.DollarsFor(config.Model, usage) ?? 0m,
            judgeDollars,
            seconds,
            error);
    }

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

        var after = judge.Usage;
        var delta = Delta(before, after);
        RunCostTracker.Record(request.Project, request.JudgeModel!, delta);
        return (scores, RunCostTracker.DollarsFor(request.JudgeModel!, delta) ?? 0m, error);
    }

    private static ClaudeUsage? Delta(ClaudeUsage? before, ClaudeUsage? after) => (before, after) switch
    {
        (_, null) => null,
        (null, var b) => b,
        var (a, b) => new ClaudeUsage(b.InputTokens - a.InputTokens, b.OutputTokens - a.OutputTokens, b.CacheReadInputTokens - a.CacheReadInputTokens, b.CacheCreationInputTokens - a.CacheCreationInputTokens),
    };
}
