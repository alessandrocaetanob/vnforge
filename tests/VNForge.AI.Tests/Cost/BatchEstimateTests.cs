using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Cost;

public sealed class BatchEstimateTests : IDisposable
{
    private static readonly ExchangeRate Rate = new(5m, new DateOnly(2026, 10, 25));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-ai-tests", Guid.NewGuid().ToString("N"));

    public BatchEstimateTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static TextUnit Line(int i) =>
        new($"yst00001:{i:D6}", TextUnitKind.Dialogue, "ハル", $"今日はいい天気ですね、{i}", null, TranslationStatus.Untranslated);

    private ProjectFolder Project() =>
        ProjectFolder.Create(_root, new ProjectConfig(
            "Teste",
            new GameConfig("D:/Jogos/Qualquer", "Qualquer.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));

    [Fact]
    public void Cada_chamada_soma_o_sistema_o_contexto_e_o_raciocinio_alem_das_linhas()
    {
        var lines = Enumerable.Range(0, 20).Select(Line).ToList();
        var plain = new TranslationBatch(lines);
        var rich = new TranslationBatch(lines)
        {
            StyleGuide = string.Join(' ', Enumerable.Repeat("Use um tom leve e informal, sem gírias.", 20)),
            Glossary = [new GlossaryEntry("魔法", "magia", "sempre assim")],
            Characters = [new CharacterInfo("ハル", "Haru", "calma", "female")],
            PreviousLines = lines.Take(3).Select(u => u with { Translation = "x", Status = TranslationStatus.Machine }).ToList(),
            NextLines = lines.Skip(10).Take(3).ToList(),
        };

        var onlyLines = CostEstimator.Estimate(lines, ClaudeModels.Opus);
        var plainBatch = CostEstimator.EstimateBatch(plain, ClaudeModels.Opus, ClaudeEffort.Medium);
        var richBatch = CostEstimator.EstimateBatch(rich, ClaudeModels.Opus, ClaudeEffort.Medium);

        Assert.True(plainBatch.Usage.Input > onlyLines.Usage.Input);
        Assert.True(plainBatch.Usage.Output > onlyLines.Usage.Output);
        Assert.True(richBatch.Usage.Input > plainBatch.Usage.Input);
        Assert.True(richBatch.Dollars > plainBatch.Dollars);
    }

    [Fact]
    public void Esforco_maior_estima_mais_raciocinio()
    {
        var batch = new TranslationBatch([Line(0)]);

        var low = CostEstimator.EstimateBatch(batch, ClaudeModels.Opus, ClaudeEffort.Low);
        var high = CostEstimator.EstimateBatch(batch, ClaudeModels.Opus, ClaudeEffort.High);

        Assert.True(high.Usage.Output > low.Usage.Output);
        Assert.Equal(low.Usage.Input, high.Usage.Input);
    }

    [Fact]
    public void Mais_lotes_para_as_mesmas_linhas_custam_mais_porque_cada_um_repete_o_contexto()
    {
        var lines = Enumerable.Range(0, 90).Select(Line).ToList();
        var one = CostEstimator.EstimateBatches([new TranslationBatch(lines)], ClaudeModels.Sonnet);
        var three = CostEstimator.EstimateBatches(lines.Chunk(30).Select(c => new TranslationBatch(c)), ClaudeModels.Sonnet);

        Assert.Equal(one.Lines, three.Lines);
        Assert.True(three.Dollars > one.Dollars);
    }

    [Fact]
    public void Lote_todo_no_cache_nao_custa_nada()
    {
        var cache = new TranslationCache();
        var batch = new TranslationBatch([Line(0)]);
        cache.Set(TranslationCache.KeyFor(batch.Lines[0], TranslationCache.ContextFor(ClaudeModels.Opus, batch)), "sim");

        var estimate = CostEstimator.EstimateBatch(batch, ClaudeModels.Opus, ClaudeEffort.High, cache);

        Assert.Equal(0m, estimate.Dollars);
        Assert.Equal(1, estimate.CachedLines);
    }

    [Fact]
    public void O_estimador_do_pedido_divide_em_lotes_do_tamanho_da_execucao()
    {
        var project = Project();
        project.SaveText("yst00001", [.. Enumerable.Range(0, 60).Select(Line)]);
        var selection = LineSelection.Build(project.LoadAllText(), null, null, TranslationQuoter.IsPending);
        var builder = new TranslationBatchBuilder([], new CharacterBook([]));
        var estimator = new RequestEstimator(selection, builder, ClaudeModels.Opus, ClaudeEffort.Medium, 30, Rate);

        var whole = estimator.Brl(selection.Pending);
        var halves = estimator.Brl(selection.Pending.Take(30).ToList()) + estimator.Brl(selection.Pending.Skip(30).ToList());

        Assert.True(whole > 0m);
        Assert.Equal(halves, whole);
        Assert.Equal(0m, estimator.Brl([]));
    }

    [Fact]
    public void O_orcamento_da_janela_considera_o_contexto_de_cada_lote()
    {
        var project = Project();
        project.SaveText("yst00001", [.. Enumerable.Range(0, 60).Select(Line)]);
        var texts = project.LoadAllText();
        var builder = new TranslationBatchBuilder([new GlossaryTerm("天気", "tempo")], new CharacterBook([]), "Tom leve.");

        var withContext = TranslationQuoter.Quote(TranslationQuoter.BatchesFor(texts, ["yst00001"], builder, 30), Rate, ClaudeEffort.Medium);
        var linesOnly = TranslationQuoter.Quote(TranslationQuoter.PendingLines(texts, ["yst00001"]), Rate);

        Assert.Equal(60, withContext.Lines);
        Assert.True(withContext.For(TranslationQuality.Careful).Brl > linesOnly.For(TranslationQuality.Careful).Brl);
    }

    [Fact]
    public void Gasto_de_modelo_sem_preco_fica_desconhecido_em_vez_de_zero()
    {
        var project = Project();
        Directory.CreateDirectory(project.CacheDirectory);

        RunCostTracker.Record(project, "modelo-que-nao-existe", new ClaudeUsage(10, 5, 0, 0));

        var totals = AiUsageLog.Load(project);
        Assert.Equal(1, totals.Runs);
        Assert.Equal(1, totals.UnpricedRuns);
        Assert.Equal(0m, totals.Dollars);
    }

    [Theory]
    [InlineData("low", ClaudeEffort.Low)]
    [InlineData(" HIGH ", ClaudeEffort.High)]
    [InlineData("medium", ClaudeEffort.Medium)]
    public void O_esforco_vem_do_texto(string text, ClaudeEffort expected)
    {
        Assert.True(ClaudeEffortText.TryParse(text, out var effort));
        Assert.Equal(expected, effort);
    }

    [Fact]
    public void Esforco_desconhecido_cai_em_medio_sem_dizer_que_entendeu()
    {
        Assert.False(ClaudeEffortText.TryParse("max", out _));
        Assert.Equal(ClaudeEffort.Medium, ClaudeEffortText.ParseOrDefault("max"));
        Assert.Equal(ClaudeEffort.Medium, ClaudeEffortText.ParseOrDefault(null));
    }
}
