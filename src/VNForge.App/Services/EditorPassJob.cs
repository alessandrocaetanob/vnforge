using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Review;
using VNForge.AI.Translation;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>Como terminou a passada de editor. Os valores em reais usam a cotação da execução.</summary>
/// <param name="Scenes">Cenas lidas pelo modelo.</param>
/// <param name="Suggestions">Ajustes que ficaram como sugestão nas linhas.</param>
/// <param name="Failed">Cenas que não voltaram ou que o modelo recusou; a tradução delas segue como estava.</param>
/// <param name="Discarded">Ajustes do modelo que a conferência descartou.</param>
/// <param name="Stopped">Por que o limite de gasto parou a passada; <see langword="null"/> quando não parou.</param>
/// <param name="SpentBrl">O que a passada gastou, em reais.</param>
/// <param name="Problem">A primeira falha, em português comum; <see langword="null"/> quando todas as cenas voltaram.</param>
public sealed record EditorPassJobResult(int Scenes, int Suggestions, int Failed, int Discarded, BudgetStop? Stopped, decimal SpentBrl, string? Problem);

/// <summary>
/// Roda a segunda passada de editor (#532) nas cenas pedidas: uma chamada por cena, em paralelo, e grava os ajustes em <c>.cache/editor-suggestions.json</c> cena a cena, assim
/// que cada uma volta. Não toca em <c>text/*.json</c>: o que a IA propõe só vira tradução quando a pessoa aceita. O <see cref="BudgetGuard"/> para a passada quando o gasto
/// do projeto chega ao limite, como na tradução.
/// </summary>
/// <param name="project">O projeto aberto.</param>
/// <param name="providerFactory">Monta o provedor para (modelo, esforço); os testes passam o provedor falso.</param>
/// <param name="settings">Cotação, limite de gasto e paralelismo.</param>
/// <param name="usageOf">Os tokens que o provedor já gastou; o padrão lê o provedor Claude.</param>
/// <param name="clock">Hora gravada em cada sugestão.</param>
public sealed class EditorPassJob(
    ProjectFolder project,
    Func<string, ClaudeEffort, IEditorPassProvider> providerFactory,
    TranslationJobSettings settings,
    Func<IEditorPassProvider, ClaudeUsage?>? usageOf = null,
    TimeProvider? clock = null)
{
    private readonly Func<IEditorPassProvider, ClaudeUsage?> usageOf = usageOf ?? (provider => (provider as ClaudeEditorPassProvider)?.Usage);

    /// <param name="scriptKeys">As cenas do escopo; as sem linha traduzida ficam de fora.</param>
    /// <param name="quality">A qualidade da tradução: a passada usa o mesmo modelo.</param>
    public async Task<EditorPassJobResult> RunAsync(IReadOnlyList<string> scriptKeys, TranslationQuality quality, IBackgroundTaskReporter reporter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scriptKeys);
        ArgumentNullException.ThrowIfNull(reporter);

        var wanted = scriptKeys.ToHashSet(StringComparer.Ordinal);
        var scenes = project.LoadAllText()
            .Where(pair => wanted.Contains(pair.Key) && pair.Value.Any(u => u.HasTranslation))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();
        if (scenes.Count == 0)
        {
            return new EditorPassJobResult(0, 0, 0, 0, null, 0m, null);
        }

        var profile = QualityProfiles.For(quality);
        var projectConfig = project.LoadConfig();
        var config = projectConfig.Ai;
        var effort = ClaudeEffortText.ParseOrDefault(config.Effort);
        var builder = new TranslationBatchBuilder(
            project.LoadGlossary(),
            new CharacterBook(project.LoadCharacters()),
            TranslationBatchBuilder.ReadStyleGuide(Path.Combine(project.Root, TranslationContextSummary.StyleGuideFileName)),
            style: projectConfig.Style,
            maxPreviousLines: config.ContextBefore,
            maxNextLines: config.ContextAfter,
            addressTerms: project.LoadAddressTerms())
        {
            Bible = project.LoadBibleOrEmpty(out _),
        };
        var provider = providerFactory(profile.Model, effort);
        var runner = new EditorPassRunner(provider, builder, clock);
        var tracker = new RunCostTracker(project, profile.Model, settings.Rate, () => usageOf(provider));
        var guard = tracker.CreateGuard(settings.Limits, _ => 0m, null);

        var gate = new object();
        var file = EditorSuggestionsFile.Load(project);
        int read = 0, suggestions = 0, failed = 0, discarded = 0, finished = 0;
        var reportedDollars = 0m;
        BudgetStop? stopped = null;
        string? problem = null;
        reporter.Report(0, "Passada de editor");

        try
        {
            await Parallel.ForEachAsync(
                scenes,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, settings.Concurrency), CancellationToken = ct },
                async (scene, token) =>
                {
                    if (guard.LimitReached)
                    {
                        lock (gate)
                        {
                            stopped = BudgetStop.LimitReached;
                        }

                        return;
                    }

                    EditorPassResult? result = null;
                    string? failure = null;
                    try
                    {
                        result = await runner.RunSceneAsync(scene.Key, scene.Value, token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        failure = ClaudeErrorMessages.Describe(e);
                    }

                    lock (gate)
                    {
                        if (result is not null)
                        {
                            file = file.ReplacingScript(scene.Key, result.Suggestions);
                            EditorSuggestionsFile.Save(project, file);
                            read++;
                            suggestions += result.Suggestions.Count;
                            discarded += result.Discarded;
                        }
                        else
                        {
                            failed++;
                            problem ??= failure;
                        }

                        finished++;
                        var now = tracker.RunDollars() ?? 0m;
                        reporter.AddCost(now - reportedDollars);
                        reportedDollars = now;
                        reporter.Report((double)finished / scenes.Count, "Passada de editor");
                    }
                }).ConfigureAwait(false);
        }
        finally
        {
            tracker.Record();
        }

        return new EditorPassJobResult(read, suggestions, failed, discarded, stopped, tracker.RunBrl, problem);
    }
}
