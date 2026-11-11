using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Review;
using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.Core.Bible;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A passada de editor (#532) em lote: lê as cenas traduzidas com o provedor falso, grava só em <c>.cache/editor-suggestions.json</c> e nunca toca na tradução.</summary>
public sealed class EditorPassJobTests : IDisposable
{
    private static readonly ExchangeRate Rate = new(5m, new DateOnly(2026, 11, 9));
    private static readonly DateTimeOffset Noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private ProjectFolder Folder(params string?[] translations)
    {
        var root = SampleProjects.Create(temp.Path, "Teste");
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [.. translations.Select((t, i) => new TextUnit($"yst00001:{i:D6}", TextUnitKind.Dialogue, "美咲", $"原文{i}", t, t is null ? TranslationStatus.Untranslated : TranslationStatus.Machine))]);
        return folder;
    }

    private static EditorPassJob Job(ProjectFolder folder, IEditorPassProvider provider, BudgetLimits? limits = null, Func<IEditorPassProvider, ClaudeUsage?>? usageOf = null) =>
        new(folder, (_, _) => provider, new TranslationJobSettings(Rate, limits ?? BudgetLimits.None, 2), usageOf, new FakeTimeProvider(Noon));

    [Fact]
    public async Task Cena_fora_do_escopo_ou_sem_traducao_nao_e_lida()
    {
        var folder = Folder(null, null);
        var fake = new FakeEditorPassProvider();

        var inScope = await Job(folder, fake).RunAsync(["yst00001"], TranslationQuality.Careful, new Reporter(), TestContext.Current.CancellationToken);
        folder.SaveText("yst00001", [.. folder.LoadText("yst00001").Select(u => u with { Translation = "Oi", Status = TranslationStatus.Machine })]);
        var outOfScope = await Job(folder, fake).RunAsync(["outra"], TranslationQuality.Careful, new Reporter(), TestContext.Current.CancellationToken);

        Assert.Equal(0, inScope.Scenes);
        Assert.Equal(0, outOfScope.Scenes);
        Assert.Empty(fake.ReceivedScenes);
    }

    [Fact]
    public async Task Falha_numa_cena_conta_e_as_outras_seguem()
    {
        var folder = Folder("Oi  você");
        folder.SaveText("yst00002", [new TextUnit("yst00002:000000", TextUnitKind.Dialogue, null, "原文", "Tudo  bem", TranslationStatus.Machine)]);
        var provider = new ThrowingFor("yst00001", new ClaudeRefusalException("O modelo recusou editar este lote."));

        var result = await Job(folder, provider).RunAsync(["yst00001", "yst00002"], TranslationQuality.Careful, new Reporter(), TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Scenes);
        Assert.Equal(1, result.Failed);
        Assert.NotNull(result.Problem);
        Assert.Equal(["yst00002:000000"], EditorSuggestionsFile.Load(folder).Suggestions.Select(s => s.UnitId));
    }

    [Fact]
    public async Task Para_quando_o_gasto_do_projeto_chega_ao_limite()
    {
        var folder = Folder("Oi  você");
        AiUsageLog.TryRecord(folder, new AiUsageTotals(0, 0, 0, 0, 100m, 1, 0));
        var fake = new FakeEditorPassProvider();

        var result = await Job(folder, fake, new BudgetLimits(10m, null)).RunAsync(["yst00001"], TranslationQuality.Careful, new Reporter(), TestContext.Current.CancellationToken);

        Assert.Equal(BudgetStop.LimitReached, result.Stopped);
        Assert.Equal(0, result.Scenes);
        Assert.Empty(fake.ReceivedScenes);
    }

    [Fact]
    public async Task Outra_grava_a_proposta_nova_no_lugar_da_antiga_e_nao_toca_na_traducao()
    {
        var folder = Folder("Oi  você", "Tudo bem");
        var job = Job(folder, new FakeEditorPassProvider());
        await job.RunAsync(["yst00001"], TranslationQuality.Careful, new Reporter(), TestContext.Current.CancellationToken);
        var before = File.ReadAllBytes(folder.TextPath("yst00001"));
        var old = Assert.Single(EditorSuggestionsFile.Load(folder).Suggestions);

        var another = await job.AnotherAsync(old, TranslationQuality.Careful, TestContext.Current.CancellationToken);

        Assert.Equal("Oi você (outra)", another!.Proposed);
        Assert.Equal(["Oi você (outra)"], EditorSuggestionsFile.Load(folder).Suggestions.Select(s => s.Proposed));
        Assert.Equal(before, File.ReadAllBytes(folder.TextPath("yst00001")));
    }

    [Fact]
    public async Task Outra_sem_proposta_nova_deixa_o_arquivo_como_estava()
    {
        var folder = Folder("Oi  você");
        var fake = new FakeEditorPassProvider { Another = (_, _) => null };
        var job = Job(folder, fake);
        await job.RunAsync(["yst00001"], TranslationQuality.Careful, new Reporter(), TestContext.Current.CancellationToken);
        var old = Assert.Single(EditorSuggestionsFile.Load(folder).Suggestions);

        Assert.Null(await job.AnotherAsync(old, TranslationQuality.Careful, TestContext.Current.CancellationToken));
        Assert.Equal([old], EditorSuggestionsFile.Load(folder).Suggestions);
        Assert.Null(await job.AnotherAsync(old with { ScriptKey = "outra" }, TranslationQuality.Careful, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Outra_acima_do_limite_de_gasto_recusa_com_mensagem_e_nao_chama_a_IA()
    {
        var folder = Folder("Oi  você");
        var fake = new FakeEditorPassProvider();
        await Job(folder, fake).RunAsync(["yst00001"], TranslationQuality.Careful, new Reporter(), TestContext.Current.CancellationToken);
        var old = Assert.Single(EditorSuggestionsFile.Load(folder).Suggestions);
        AiUsageLog.TryRecord(folder, new AiUsageTotals(0, 0, 0, 0, 100m, 1, 0));
        var asked = new FakeEditorPassProvider { Another = (_, _) => throw new InvalidOperationException("não devia chamar") };

        var error = await Assert.ThrowsAsync<LineSuggestionException>(() =>
            Job(folder, asked, new BudgetLimits(10m, null)).AnotherAsync(old, TranslationQuality.Careful, TestContext.Current.CancellationToken));

        Assert.Contains("limite", error.Message);
        Assert.Equal([old], EditorSuggestionsFile.Load(folder).Suggestions);
    }

    private sealed class HookedProvider(Action<TranslationBatch> onScene) : IEditorPassProvider
    {
        private readonly FakeEditorPassProvider inner = new();

        public Action? OnAnother { get; set; }

        public Task<IReadOnlyList<EditorPassProposal>> ReviewSceneAsync(TranslationBatch scene, CancellationToken ct)
        {
            onScene(scene);
            return inner.ReviewSceneAsync(scene, ct);
        }

        public Task<EditorPassProposal?> AnotherAsync(TranslationBatch line, string rejected, CancellationToken ct)
        {
            OnAnother?.Invoke();
            return inner.AnotherAsync(line, rejected, ct);
        }
    }

    private sealed class ThrowingFor(string scriptKey, Exception error) : IEditorPassProvider
    {
        private readonly FakeEditorPassProvider inner = new();

        public Task<EditorPassProposal?> AnotherAsync(TranslationBatch line, string rejected, CancellationToken ct) => inner.AnotherAsync(line, rejected, ct);

        public Task<IReadOnlyList<EditorPassProposal>> ReviewSceneAsync(TranslationBatch scene, CancellationToken ct) =>
            scene.Lines[0].Id.StartsWith(scriptKey + ":", StringComparison.Ordinal) ? throw error : inner.ReviewSceneAsync(scene, ct);
    }

    private sealed class Reporter : IBackgroundTaskReporter
    {
        public decimal Cost { get; private set; }

        public double? LastFraction { get; private set; }

        public void Report(double? fraction, string? message = null) => LastFraction = fraction;

        public void AddCost(decimal usd) => Cost += usd;
    }
}
