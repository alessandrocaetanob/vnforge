using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Review;
using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class ReviewCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;
    private FakeReviewProvider _provider = new();
    private string? _requestedModel;

    public ReviewCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        _project.SaveText("yst00001",
        [
            Unit("yst00001", 1, TranslationStatus.Untranslated),
            Unit("yst00001", 2, TranslationStatus.Machine),
            Unit("yst00001", 3, TranslationStatus.Reviewed),
            Unit("yst00001", 4, TranslationStatus.Machine),
            Unit("yst00001", 5, TranslationStatus.Translated),
        ]);
        _project.SaveText("yst00002", [Unit("yst00002", 1, TranslationStatus.Machine)]);
        _project.SaveText("abc", [Unit("abc", 1, TranslationStatus.Machine)]);
        _cli.Cli.ReviewProviderFactory = (model, _) =>
        {
            _requestedModel = model;
            return _provider;
        };
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(string key, int n, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, "美咲", $"原文{n}", status == TranslationStatus.Untranslated ? null : $"tradução {n}", status)
        {
            Origin = status == TranslationStatus.Machine ? new TranslationOrigin("anthropic", ClaudeModels.Opus, new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)) : null,
        };

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync(["review", "--project", _projectPath, .. args], TestContext.Current.CancellationToken);

    private TextUnit Line(string key, int index) => _project.LoadText(key)[index];

    private string AllTextJson() =>
        string.Concat(_project.LoadAllText().OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => TextUnitJson.SerializeList(p.Value)));

    [Fact]
    public async Task Grava_a_sugestao_em_notes_sem_mudar_translation_status_nem_origin()
    {
        var before = _project.LoadAllText().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        var result = await Run("--status", "all");

        Assert.Equal(0, result.ExitCode);
        foreach (var (key, units) in before)
        {
            var after = _project.LoadText(key);
            Assert.Equal(units.Count, after.Count);
            for (var i = 0; i < units.Count; i++)
            {
                Assert.Equal(units[i].Translation, after[i].Translation);
                Assert.Equal(units[i].Status, after[i].Status);
                Assert.Equal(units[i].Origin, after[i].Origin);
                Assert.Equal(units[i].Source, after[i].Source);
            }
        }

        Assert.Equal("[IA] Conferir: tradução 2", Line("yst00001", 1).Notes);
        Assert.Equal("[IA] Conferir: tradução 5", Line("yst00001", 4).Notes);
        Assert.Equal(string.Empty, Line("yst00001", 0).Notes);
    }

    [Fact]
    public async Task Sem_status_revisa_so_as_linhas_machine()
    {
        await Run();

        Assert.Equal("[IA] Conferir: tradução 2", Line("yst00001", 1).Notes);
        Assert.Equal("[IA] Conferir: tradução 4", Line("yst00001", 3).Notes);
        Assert.Equal("[IA] Conferir: tradução 1", Line("abc", 0).Notes);
        Assert.Equal(string.Empty, Line("yst00001", 2).Notes);
        Assert.Equal(string.Empty, Line("yst00001", 4).Notes);
        Assert.Equal(string.Empty, Line("yst00001", 0).Notes);
    }

    [Fact]
    public async Task O_lote_leva_a_traducao_para_o_provedor()
    {
        await Run("--files", "abc");

        var line = Assert.Single(Assert.Single(_provider.ReceivedBatches).Lines);
        Assert.Equal("tradução 1", line.Translation);
        Assert.Equal(ClaudeModels.Opus, _requestedModel);
    }

    [Fact]
    public async Task Nova_revisao_troca_a_nota_da_ia_e_preserva_a_nota_humana()
    {
        var units = _project.LoadText("abc");
        _project.SaveText("abc", [units[0] with { Notes = "Falar com o diretor\n[IA] nota antiga" }]);

        await Run("--files", "abc");

        Assert.Equal("Falar com o diretor\n[IA] Conferir: tradução 1", Line("abc", 0).Notes);
    }

    [Fact]
    public async Task Linha_sem_ressalva_nao_grava_nota_nem_reescreve_o_arquivo()
    {
        _provider = new FakeReviewProvider(_ => string.Empty);
        var before = AllTextJson();

        var result = await Run("--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(before, AllTextJson());
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("suggestions").GetInt32());
        Assert.Equal(4, data.GetProperty("clean").GetInt32());
    }

    [Fact]
    public async Task Files_e_limit_filtram_as_linhas()
    {
        var result = await Run("--files", "yst0000*", "--limit", "2", "--json");

        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal(2, doc.RootElement.GetProperty("data").GetProperty("suggestions").GetInt32());
        Assert.Equal(string.Empty, Line("abc", 0).Notes);
        Assert.Equal(string.Empty, Line("yst00002", 0).Notes);
    }

    [Fact]
    public async Task Dry_run_estima_sem_chamar_o_provedor_nem_gravar_nada()
    {
        var before = AllTextJson();

        var result = await Run("--dry-run", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(_provider.ReceivedBatches);
        Assert.Equal(before, AllTextJson());
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("dryRun").GetBoolean());
        Assert.Equal(4, data.GetProperty("estimate").GetProperty("lines").GetInt32());
        Assert.True(data.GetProperty("estimate").GetProperty("dollars").GetDecimal() > 0);
    }

    [Fact]
    public async Task Resultado_em_portugues_diz_que_traducoes_nao_mudaram()
    {
        var result = await Run();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("4 linhas revisadas de 4", result.Output);
        Assert.Contains("4 sugestões gravadas", result.Output);
        Assert.Contains("As traduções e os status não foram alterados.", result.Output);
        Assert.Contains("100%", result.Error);
    }

    [Fact]
    public async Task Nada_a_revisar_avisa_em_portugues()
    {
        var result = await Run("--status", "approved");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Nada a revisar", result.Output);
    }

    [Fact]
    public async Task Lote_que_falha_sai_com_1_e_nao_grava_nota()
    {
        _cli.Cli.ReviewProviderFactory = (_, _) => new FailingProvider();

        var result = await Run("--files", "abc", "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, Line("abc", 0).Notes);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal(1, doc.RootElement.GetProperty("data").GetProperty("failed").GetArrayLength());
    }

    [Fact]
    public async Task Linha_recusada_fica_sem_nota_e_aparece_no_resultado()
    {
        _cli.Cli.ReviewProviderFactory = (_, _) => new RefusingProvider();

        var result = await Run("--files", "abc", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, Line("abc", 0).Notes);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal("abc:000001", doc.RootElement.GetProperty("data").GetProperty("refused")[0].GetString());
    }

    [Fact]
    public async Task Status_desconhecido_e_erro_de_uso()
    {
        var result = await Run("--status", "nada");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Status desconhecido", result.Error);
    }

    [Fact]
    public async Task Sem_chave_de_api_avisa_em_portugues_e_sai_com_1()
    {
        Assert.SkipWhen(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")), "ANTHROPIC_API_KEY definida nesta máquina.");
        _cli.Cli.ReviewProviderFactory = null;

        var result = await Run();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Sem chave de API", result.Error);
    }

    [Fact]
    public async Task Glossario_e_guia_de_estilo_chegam_ao_lote()
    {
        _project.SaveGlossary([new VNForge.Core.Glossary.GlossaryTerm("原文1", "texto 1", null)]);
        File.WriteAllText(Path.Combine(_projectPath, "style.md"), "Tom informal.");

        await Run("--files", "abc");

        var batch = Assert.Single(_provider.ReceivedBatches);
        Assert.Equal("Tom informal.", batch.StyleGuide);
        Assert.Equal("texto 1", Assert.Single(batch.Glossary).Target);
    }

    private sealed class FailingProvider : IReviewProvider
    {
        public Task<IReadOnlyList<ReviewResult>> ReviewAsync(TranslationBatch batch, CancellationToken ct) =>
            throw new ArgumentException("Falha simulada.");
    }

    private sealed class RefusingProvider : IReviewProvider
    {
        public Task<IReadOnlyList<ReviewResult>> ReviewAsync(TranslationBatch batch, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ReviewResult>>([.. batch.Lines.Select(l => new ReviewResult(l.Id, string.Empty, Refused: true))]);
    }
}
