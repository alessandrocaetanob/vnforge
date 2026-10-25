using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>O que a janela "Traduzir com IA" pediu: o escopo, a qualidade e os scripts do escopo.</summary>
public sealed record TranslationRequest(TranslationScope Scope, TranslationQuality Quality, IReadOnlyList<string> ScriptKeys);

/// <summary>A cotação, o limite de gasto e as chamadas em paralelo de uma execução. <paramref name="Delay"/> troca a espera entre tentativas (os testes não esperam).</summary>
public sealed record TranslationJobSettings(ExchangeRate Rate, BudgetLimits Limits, int Concurrency, Func<TimeSpan, CancellationToken, Task>? Delay = null);

/// <summary>Como terminou uma execução. Os valores em reais usam a cotação da execução.</summary>
/// <param name="Translated">Linhas gravadas como tradução da IA.</param>
/// <param name="Failed">Linhas dos lotes que não voltaram, mesmo depois das tentativas.</param>
/// <param name="Refused">Linhas que a IA não traduziu ou que não passaram nas regras de validação.</param>
/// <param name="Stopped">Por que o limite de gasto parou o pedido; <see langword="null"/> quando não parou.</param>
/// <param name="SpentBrl">O que a execução gastou, em reais.</param>
/// <param name="Problem">A primeira falha, em português comum; <see langword="null"/> quando todos os lotes voltaram.</param>
public sealed record TranslationJobResult(int Translated, int Failed, int Refused, BudgetStop? Stopped, decimal SpentBrl, string? Problem);

/// <summary>
/// Traduz com a IA as linhas pendentes dos scripts pedidos e grava cada lote em <c>text/*.json</c> assim que volta, como <c>vnf translate</c>.
/// O <see cref="BudgetGuard"/> para o pedido quando o gasto do projeto mais o desta execução chega ao limite.
/// </summary>
/// <param name="project">O projeto aberto.</param>
/// <param name="providerFactory">Monta o provedor para (modelo, esforço); os testes passam o provedor falso.</param>
/// <param name="settings">Cotação, limite de gasto e paralelismo.</param>
/// <param name="confirm">Responde à pergunta do <see cref="BudgetGuard"/> quando o pedido passa do valor de aviso; sem ele, esse pedido é recusado. O app responde "sim", porque a janela já mostrou o preço e a pessoa confirmou.</param>
/// <param name="usageOf">Os tokens que o provedor já gastou; o padrão lê o provedor Claude.</param>
/// <param name="clock">Hora gravada na origem de cada linha.</param>
public sealed class TranslationJob(
    ProjectFolder project,
    Func<string, ClaudeEffort, ITranslationProvider> providerFactory,
    TranslationJobSettings settings,
    Func<BudgetQuestion, CancellationToken, Task<bool>>? confirm = null,
    Func<ITranslationProvider, ClaudeUsage?>? usageOf = null,
    TimeProvider? clock = null)
{
    private const string ProviderName = "anthropic";
    private const string CacheFileName = "translations.json";
    private const int PreviousLinesWindow = 30;

    private readonly Func<ITranslationProvider, ClaudeUsage?> usageOf = usageOf ?? (provider => (provider as ClaudeTranslationProvider)?.Usage);
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public async Task<TranslationJobResult> RunAsync(TranslationRequest request, IBackgroundTaskReporter reporter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reporter);

        var profile = QualityProfiles.For(request.Quality);
        var selection = Selection.Build(project.LoadAllText(), request.ScriptKeys);
        if (selection.Pending.Count == 0)
        {
            return new TranslationJobResult(0, 0, 0, null, 0m, null);
        }

        var config = project.LoadConfig().Ai;
        var batchSize = Math.Clamp(config.BatchSize, TranslationRunOptions.MinBatchSize, TranslationRunOptions.MaxBatchSize);
        var builder = new TranslationBatchBuilder(
            project.LoadGlossary(),
            new CharacterBook(project.LoadCharacters()),
            TranslationBatchBuilder.ReadStyleGuide(Path.Combine(project.Root, TranslationContextSummary.StyleGuideFileName)));
        var cachePath = Path.Combine(project.CacheDirectory, CacheFileName);
        var cache = TranslationCache.Load(cachePath);
        var raw = providerFactory(profile.Model, ParseEffort(config.Effort));
        var chain = new CachingTranslationProvider(new ValidatingTranslationProvider(raw), cache, profile.Model);

        var baseline = AiUsageLog.Load(project).Dollars;
        var guard = new BudgetGuard(
            settings.Limits,
            () => settings.Rate.ToBrl(baseline + RunDollars(raw, profile.Model)),
            lines => CostEstimator.Quote(lines, profile.Model, settings.Rate).Brl,
            confirm);
        var runner = new TranslationRunner(
            chain,
            new TranslationRunOptions
            {
                BatchSize = batchSize,
                MaxConcurrency = Math.Max(1, settings.Concurrency),
                Budget = guard,
                Delay = settings.Delay ?? new TranslationRunOptions().Delay,
            });

        var origin = new TranslationOrigin(ProviderName, profile.Model, clock.GetUtcNow());
        var counters = new Counters();
        var reportedDollars = 0m;
        var gate = new object();
        reporter.Report(0, "Traduzindo");

        void OnBatch(IReadOnlyList<TranslationResult> results)
        {
            lock (gate)
            {
                selection.ApplyAndSave(project, results, origin, counters);
                var now = RunDollars(raw, profile.Model);
                reporter.AddCost(now - reportedDollars);
                reportedDollars = now;
                reporter.Report((double)(counters.Translated + counters.Refused) / selection.Pending.Count, "Traduzindo");
            }
        }

        TranslationRunResult run;
        try
        {
            run = await runner.RunAsync(selection.Pending, lines => BuildBatch(selection, builder, lines, gate), OnBatch, null, ct).ConfigureAwait(false);
        }
        finally
        {
            if (cache.Count > 0)
            {
                cache.Save(cachePath);
            }

            RecordUsage(raw, profile.Model);
        }

        if (run.CallbackError is { } callbackError)
        {
            throw new IOException($"Não foi possível gravar a tradução: {callbackError.Message}", callbackError);
        }

        ct.ThrowIfCancellationRequested();
        var failed = run.Failures.Sum(f => f.LineIds.Count);
        var problem = run.Failures.Count > 0 ? ClaudeErrorMessages.Describe(run.Failures[0].Error) : null;
        return new TranslationJobResult(counters.Translated, failed, counters.Refused, run.BudgetStop, settings.Rate.ToBrl(RunDollars(raw, profile.Model)), problem);
    }

    private static TranslationBatch BuildBatch(Selection selection, TranslationBatchBuilder builder, IReadOnlyList<TextUnit> lines, object gate)
    {
        lock (gate)
        {
            return selection.BuildBatch(builder, lines);
        }
    }

    private decimal RunDollars(ITranslationProvider provider, string model) =>
        usageOf(provider) is { } u
            ? ModelPricing.For(model).Cost(new TokenUsage(u.InputTokens, u.OutputTokens, u.CacheReadInputTokens, u.CacheCreationInputTokens))
            : 0m;

    private void RecordUsage(ITranslationProvider provider, string model)
    {
        if (usageOf(provider) is not { } u)
        {
            return;
        }

        AiUsageLog.TryRecord(project, new AiUsageTotals(
            u.InputTokens, u.OutputTokens, u.CacheReadInputTokens, u.CacheCreationInputTokens, RunDollars(provider, model), 1));
    }

    private static ClaudeEffort ParseEffort(string text) => text.Trim().ToLowerInvariant() switch
    {
        "low" => ClaudeEffort.Low,
        "high" => ClaudeEffort.High,
        _ => ClaudeEffort.Medium,
    };

    private sealed class Counters
    {
        public int Translated { get; set; }

        public int Refused { get; set; }
    }

    /// <summary>As linhas pendentes e o mapa para gravá-las de volta no <c>text/*.json</c> de origem.</summary>
    private sealed class Selection
    {
        private readonly Dictionary<string, List<TextUnit>> files = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> keyById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> indexById = new(StringComparer.Ordinal);

        public List<TextUnit> Pending { get; } = [];

        public static Selection Build(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, IEnumerable<string> keys)
        {
            var selection = new Selection();
            foreach (var key in keys.Where(texts.ContainsKey))
            {
                var units = texts[key];
                var wanted = units.Where(u => u.Status == TranslationStatus.Untranslated && !string.IsNullOrWhiteSpace(u.Source)).ToList();
                if (wanted.Count == 0)
                {
                    continue;
                }

                selection.files[key] = [.. units];
                for (var i = 0; i < units.Count; i++)
                {
                    selection.keyById[units[i].Id] = key;
                    selection.indexById[units[i].Id] = i;
                }

                selection.Pending.AddRange(wanted);
            }

            return selection;
        }

        public TranslationBatch BuildBatch(TranslationBatchBuilder builder, IReadOnlyList<TextUnit> lines)
        {
            var first = lines[0].Id;
            var previous = files[keyById[first]].Take(indexById[first]).TakeLast(PreviousLinesWindow).ToList();
            return builder.Build(lines, previous);
        }

        public void ApplyAndSave(ProjectFolder folder, IReadOnlyList<TranslationResult> results, TranslationOrigin origin, Counters counters)
        {
            var touched = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var result in results)
            {
                if (result.Refused || string.IsNullOrWhiteSpace(result.Translation))
                {
                    counters.Refused++;
                    continue;
                }

                var key = keyById[result.Id];
                var list = files[key];
                var index = indexById[result.Id];
                list[index] = list[index] with
                {
                    Translation = result.Translation,
                    Status = TranslationStatus.Machine,
                    Origin = origin,
                    PreviousSource = null,
                };
                touched.Add(key);
                counters.Translated++;
            }

            foreach (var key in touched)
            {
                folder.SaveText(key, files[key]);
            }
        }
    }
}
