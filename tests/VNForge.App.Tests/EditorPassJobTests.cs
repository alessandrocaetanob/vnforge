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
    public async Task Grava_os_ajustes_como_sugestao_e_deixa_a_traducao_como_estava()
    {
        var folder = Folder("Oi  você", "Tudo bem");
        var before = File.ReadAllBytes(folder.TextPath("yst00001"));

        var result = await Job(folder, new FakeEditorPassProvider()).RunAsync(["yst00001"], TranslationQuality.Careful, new Reporter(), TestContext.Current.CancellationToken);

        Assert.Equal(new EditorPassJobResult(1, 1, 0, 0, null, 0m, null), result);
        var suggestion = Assert.Single(EditorSuggestionsFile.Load(folder).Suggestions);
        Assert.Equal(new EditorSuggestion("yst00001", "yst00001:000000", EditorSuggestionKind.Fluency, "Oi  você", "Oi você", "Tira o espaço duplo.", Noon), suggestion);
        Assert.Equal(before, File.ReadAllBytes(folder.TextPath("yst00001")));
    }

    [Fact]
    public async Task Usa_o_modelo_da_qualidade_e_leva_o_resumo_da_cena_da_biblia()
    {
        var folder = Folder("Oi", "Tudo bem");
        folder.SaveBible(GameBible.From([new BibleScene("yst00001") { Summary = "Duas amigas se encontram no portão." }]));
        var fake = new FakeEditorPassProvider();
        string? model = null;
        var job = new EditorPassJob(folder, (m, _) => { model = m; return fake; }, new TranslationJobSettings(Rate, BudgetLimits.None, 1));

        await job.RunAsync(["yst00001"], TranslationQuality.Fast, new Reporter(), TestContext.Current.CancellationToken);

        Assert.Equal(ClaudeModels.Haiku, model);
        var scene = Assert.Single(fake.ReceivedScenes);
        Assert.Equal("Duas amigas se encontram no portão.", Assert.Single(scene.Scenes).Summary);
    }

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
    public async Task Uma_passada_nova_troca_as_sugestoes_antigas_da_cena()
    {
        var folder = Folder("Oi  você", "Tudo  bem");
        var job = Job(folder, new FakeEditorPassProvider());
        await job.RunAsync(["yst00001"], TranslationQuality.Careful, new Reporter(), TestContext.Current.CancellationToken);
        Assert.Equal(2, EditorSuggestionsFile.Load(folder).Suggestions.Count);

        folder.SaveText("yst00001", [.. folder.LoadText("yst00001").Select(u => u with { Translation = u.Translation!.Replace("  ", " ", StringComparison.Ordinal) })]);
        await job.RunAsync(["yst00001"], TranslationQuality.Careful, new Reporter(), TestContext.Current.CancellationToken);

        Assert.Empty(EditorSuggestionsFile.Load(folder).Suggestions);
        Assert.False(File.Exists(EditorSuggestionsFile.PathFor(folder)));
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
    public async Task Registra_o_gasto_no_projeto_e_no_relatorio_da_tarefa()
    {
        var folder = Folder("Oi  você");
        var reporter = new Reporter();

        var result = await Job(folder, new FakeEditorPassProvider(), usageOf: _ => new ClaudeUsage(1_000_000, 100_000, 0, 0))
            .RunAsync(["yst00001"], TranslationQuality.Careful, reporter, TestContext.Current.CancellationToken);

        Assert.True(result.SpentBrl > 0m);
        Assert.True(reporter.Cost > 0m);
        Assert.True(AiUsageLog.Load(folder).Dollars > 0m);
        Assert.Equal(1d, reporter.LastFraction);
    }

    [Fact]
    public async Task Cancelar_interrompe_sem_gravar_sugestao()
    {
        var folder = Folder("Oi  você");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Job(folder, new FakeEditorPassProvider()).RunAsync(["yst00001"], TranslationQuality.Careful, new Reporter(), cancellation.Token));

        Assert.False(File.Exists(EditorSuggestionsFile.PathFor(folder)));
    }

    private sealed class ThrowingFor(string scriptKey, Exception error) : IEditorPassProvider
    {
        private readonly FakeEditorPassProvider inner = new();

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
