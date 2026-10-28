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
/// <param name="Skipped">Linhas que a pessoa mudou durante a execução (traduziu, aprovou, trocou o original) e que a IA não tocou.</param>
public sealed record TranslationJobResult(int Translated, int Failed, int Refused, BudgetStop? Stopped, decimal SpentBrl, string? Problem, int Skipped = 0);

/// <summary>
/// Traduz com a IA as linhas pendentes dos scripts pedidos e grava cada lote em <c>text/*.json</c> assim que volta, como <c>vnf translate</c>
/// (mesmos <see cref="LineSelection"/>, <see cref="RunCostTracker"/> e estimativa por lote). O <see cref="BudgetGuard"/> para o pedido quando o
/// gasto do projeto mais o desta execução chega ao limite.
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

    private readonly Func<ITranslationProvider, ClaudeUsage?> usageOf = usageOf ?? (provider => (provider as ClaudeTranslationProvider)?.Usage);
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public async Task<TranslationJobResult> RunAsync(TranslationRequest request, IBackgroundTaskReporter reporter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reporter);

        var profile = QualityProfiles.For(request.Quality);
        var scriptKeys = request.ScriptKeys.ToHashSet(StringComparer.Ordinal);
        var selection = LineSelection.Build(project.LoadAllText(), scriptKeys.Contains, null, TranslationQuoter.IsPending);
        var pending = selection.Pending;
        if (pending.Count == 0)
        {
            return new TranslationJobResult(0, 0, 0, null, 0m, null);
        }

        var projectConfig = project.LoadConfig();
        var config = projectConfig.Ai;
        var effort = ClaudeEffortText.ParseOrDefault(config.Effort);
        var batchSize = Math.Clamp(config.BatchSize, TranslationRunOptions.MinBatchSize, TranslationRunOptions.MaxBatchSize);
        var builder = new TranslationBatchBuilder(
            project.LoadGlossary(),
            new CharacterBook(project.LoadCharacters()),
            TranslationBatchBuilder.ReadStyleGuide(Path.Combine(project.Root, TranslationContextSummary.StyleGuideFileName)),
            style: projectConfig.Style);
        var cachePath = Path.Combine(project.CacheDirectory, CacheFileName);
        var cache = TranslationCache.Load(cachePath);
        var raw = providerFactory(profile.Model, effort);
        var chain = KeyboardPunctuationProvider.Wrap(new CachingTranslationProvider(new ValidatingTranslationProvider(raw), cache, profile.Model), projectConfig);

        var tracker = new RunCostTracker(project, profile.Model, settings.Rate, () => usageOf(raw));
        var estimator = new RequestEstimator(selection, builder, profile.Model, effort, batchSize, settings.Rate, cache);
        var runner = new TranslationRunner(
            chain,
            new TranslationRunOptions
            {
                BatchSize = batchSize,
                MaxConcurrency = Math.Max(1, settings.Concurrency),
                Budget = tracker.CreateGuard(settings.Limits, estimator.Brl, confirm),
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
                var saved = selection.ApplyAndSave(project, results, (result, unit) => Apply(counters, origin, result, unit));
                counters.Skipped += saved.Skipped;
                var now = tracker.RunDollars() ?? 0m;
                reporter.AddCost(now - reportedDollars);
                reportedDollars = now;
                reporter.Report((double)(counters.Translated + counters.Refused + counters.Skipped) / pending.Count, "Traduzindo");
            }
        }

        TranslationRunResult run;
        try
        {
            run = await runner.RunAsync(pending, lines => selection.BuildBatch(builder, lines), OnBatch, null, ct).ConfigureAwait(false);
        }
        finally
        {
            if (cache.Count > 0)
            {
                cache.Save(cachePath);
            }

            tracker.Record();
        }

        if (run.CallbackError is { } callbackError)
        {
            throw new IOException($"Não foi possível gravar a tradução: {callbackError.Message}", callbackError);
        }

        ct.ThrowIfCancellationRequested();
        var failed = run.Failures.Sum(f => f.LineIds.Count);
        var problem = run.Failures.Count > 0 ? ClaudeErrorMessages.Describe(run.Failures[0].Error) : null;
        return new TranslationJobResult(counters.Translated, failed, counters.Refused, run.BudgetStop, tracker.RunBrl, problem, counters.Skipped);
    }

    private static TextUnit? Apply(Counters counters, TranslationOrigin origin, TranslationResult result, TextUnit unit)
    {
        if (result.Refused || string.IsNullOrWhiteSpace(result.Translation))
        {
            counters.Refused++;
            return null;
        }

        counters.Translated++;
        return unit with
        {
            Translation = result.Translation,
            Status = TranslationStatus.Machine,
            Origin = origin,
            PreviousSource = null,
            Doubt = result.Doubt ?? (string.Equals(unit.Translation, result.Translation, StringComparison.Ordinal) ? unit.Doubt : null),
        };
    }

    private sealed class Counters
    {
        public int Translated { get; set; }

        public int Refused { get; set; }

        public int Skipped { get; set; }
    }
}
