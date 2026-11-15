using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Cost;

[Trait("Risco", "R5")]
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
}
