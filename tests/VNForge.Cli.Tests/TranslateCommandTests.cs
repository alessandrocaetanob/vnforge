using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class TranslateCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;
    private FakeTranslationProvider _provider = new();
    private string? _requestedModel;
    private ClaudeEffort? _requestedEffort;

    public TranslateCommandTests()
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
            Unit("yst00001", 4, TranslationStatus.Untranslated),
        ]);
        _project.SaveText("yst00002", [Unit("yst00002", 1, TranslationStatus.Untranslated)]);
        _project.SaveText("abc", [Unit("abc", 1, TranslationStatus.Untranslated)]);
        _cli.Cli.TranslationProviderFactory = (model, effort) =>
        {
            _requestedModel = model;
            _requestedEffort = effort;
            return _provider;
        };
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(string key, int n, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, "美咲", $"原文{n}", status == TranslationStatus.Untranslated ? null : "antiga", status);

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync(["translate", "--project", _projectPath, .. args], TestContext.Current.CancellationToken);

    private TextUnit Line(string key, int index) => _project.LoadText(key)[index];

    [Fact]
    public async Task Traduz_as_linhas_sem_traducao_e_grava_status_machine_com_origem()
    {
        var result = await Run();

        Assert.Equal(0, result.ExitCode);
        var line = Line("yst00001", 0);
        Assert.Equal("[PT] 原文1", line.Translation);
        Assert.Equal(TranslationStatus.Machine, line.Status);
        Assert.NotNull(line.Origin);
        Assert.Equal("anthropic", line.Origin.Provider);
        Assert.Equal(ClaudeModels.Opus, line.Origin.Model);
        Assert.Equal("[PT] 原文1", Line("abc", 0).Translation);
        Assert.Equal("[PT] 原文1", Line("yst00002", 0).Translation);
    }

    [Fact]
    public async Task Padrao_nao_toca_em_linhas_machine_nem_revisadas()
    {
        await Run();

        Assert.Equal("antiga", Line("yst00001", 1).Translation);
        Assert.Equal(TranslationStatus.Machine, Line("yst00001", 1).Status);
        Assert.Equal("antiga", Line("yst00001", 2).Translation);
        Assert.Equal(TranslationStatus.Reviewed, Line("yst00001", 2).Status);
    }

    [Fact]
    public async Task Status_machine_refaz_so_as_linhas_machine()
    {
        await Run("--status", "machine");

        Assert.Equal("[PT] 原文2", Line("yst00001", 1).Translation);
        Assert.Null(Line("yst00001", 0).Translation);
        Assert.Equal("antiga", Line("yst00001", 2).Translation);
    }

    [Fact]
    public async Task Status_all_inclui_sem_traducao_e_machine_mas_preserva_revisadas()
    {
        await Run("--status", "all");

        Assert.Equal("[PT] 原文1", Line("yst00001", 0).Translation);
        Assert.Equal("[PT] 原文2", Line("yst00001", 1).Translation);
        Assert.Equal("antiga", Line("yst00001", 2).Translation);
        Assert.Equal(TranslationStatus.Reviewed, Line("yst00001", 2).Status);
    }

    [Fact]
    public async Task Files_filtra_os_scripts_por_glob()
    {
        await Run("--files", "yst0000*");

        Assert.Equal("[PT] 原文1", Line("yst00002", 0).Translation);
        Assert.Null(Line("abc", 0).Translation);
    }

    [Fact]
    public async Task Limit_para_depois_de_n_linhas()
    {
        var result = await Run("--limit", "2", "--json");

        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal(2, doc.RootElement.GetProperty("data").GetProperty("translated").GetInt32());
        Assert.Equal("[PT] 原文1", Line("abc", 0).Translation);
        Assert.Equal("[PT] 原文1", Line("yst00001", 0).Translation);
        Assert.Null(Line("yst00001", 3).Translation);
        Assert.Null(Line("yst00002", 0).Translation);
    }

    [Fact]
    public async Task Dry_run_estima_sem_chamar_o_provedor_nem_gravar_nada()
    {
        var before = File.ReadAllText(_project.TextPath("yst00001"));

        var result = await Run("--dry-run", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(_provider.ReceivedBatches);
        Assert.Equal(before, File.ReadAllText(_project.TextPath("yst00001")));
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("dryRun").GetBoolean());
        Assert.Equal(4, data.GetProperty("estimate").GetProperty("lines").GetInt32());
        Assert.True(data.GetProperty("estimate").GetProperty("inputTokens").GetInt64() > 0);
        Assert.True(data.GetProperty("estimate").GetProperty("dollars").GetDecimal() > 0);
    }

    [Fact]
    public async Task Dry_run_nao_pede_chave_de_api()
    {
        _cli.Cli.TranslationProviderFactory = null;

        var result = await Run("--dry-run");

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Sem_chave_de_api_avisa_em_portugues_e_sai_com_1()
    {
        Assert.SkipWhen(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")), "ANTHROPIC_API_KEY definida nesta máquina.");
        _cli.Cli.TranslationProviderFactory = null;

        var result = await Run();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ANTHROPIC_API_KEY", result.Error);
        Assert.Null(Line("abc", 0).Translation);
    }

    [Fact]
    public async Task Model_effort_batch_size_e_concurrency_chegam_ao_provedor()
    {
        var result = await Run("--model", "claude-haiku-5-5", "--effort", "low", "--batch-size", "20", "--concurrency", "1", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ClaudeModels.Haiku, _requestedModel);
        Assert.Equal(ClaudeEffort.Low, _requestedEffort);
        Assert.Equal(ClaudeModels.Haiku, Line("abc", 0).Origin!.Model);
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(20, data.GetProperty("batchSize").GetInt32());
        Assert.Equal(1, data.GetProperty("concurrency").GetInt32());
    }

    [Fact]
    public async Task Lotes_respeitam_o_batch_size()
    {
        var lines = Enumerable.Range(1, 45).Select(n => Unit("grande", n, TranslationStatus.Untranslated)).ToList();
        _project.SaveText("grande", lines);

        await Run("--files", "grande", "--batch-size", "20", "--concurrency", "1");

        Assert.Equal([20, 20, 5], _provider.ReceivedBatches.Select(b => b.Lines.Count));
    }

    [Theory]
    [InlineData("--model", "gpt-x")]
    [InlineData("--effort", "extreme")]
    [InlineData("--status", "tudo")]
    [InlineData("--batch-size", "5")]
    [InlineData("--batch-size", "99")]
    [InlineData("--concurrency", "0")]
    [InlineData("--limit", "-1")]
    public async Task Valores_invalidos_saem_com_1_sem_gravar(string option, string value)
    {
        var result = await Run(option, value);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(_provider.ReceivedBatches);
        Assert.Null(Line("abc", 0).Translation);
    }

    [Fact]
    public async Task Segunda_execucao_nao_tem_o_que_traduzir()
    {
        await Run();
        var calls = _provider.ReceivedBatches.Count;

        var result = await Run("--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(calls, _provider.ReceivedBatches.Count);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("lines").GetInt32());
    }

    [Fact]
    public async Task Status_machine_de_novo_reaproveita_o_cache_sem_chamar_o_provedor()
    {
        await Run("--files", "abc");
        var calls = _provider.ReceivedBatches.Count;

        var result = await Run("--files", "abc", "--status", "machine", "--json");

        Assert.Equal(calls, _provider.ReceivedBatches.Count);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal(1, doc.RootElement.GetProperty("data").GetProperty("cached").GetInt32());
    }

    [Fact]
    public async Task Json_traz_o_resultado_e_o_texto_mostra_o_progresso()
    {
        var json = await Run("--json");
        using var doc = JsonDocument.Parse(json.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(4, data.GetProperty("translated").GetInt32());
        Assert.Equal(0, data.GetProperty("refused").GetArrayLength());
        Assert.Equal(0, data.GetProperty("failed").GetArrayLength());
        Assert.Equal(string.Empty, json.Error);

        _project.SaveText("novo", [Unit("novo", 1, TranslationStatus.Untranslated)]);
        var text = await Run();
        Assert.Contains("100%", text.Error);
        Assert.Contains("novo", text.Error);
        Assert.Contains("1 linha", text.Output);
    }

    [Fact]
    public async Task Lote_que_falha_deixa_as_linhas_sem_traducao_e_sai_com_1()
    {
        _cli.Cli.TranslationProviderFactory = (_, _) => new FailingProvider();

        var result = await Run("--files", "abc", "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Null(Line("abc", 0).Translation);
        using var doc = JsonDocument.Parse(result.Output);
        var failed = doc.RootElement.GetProperty("data").GetProperty("failed");
        Assert.Equal(1, failed.GetArrayLength());
        Assert.Equal("abc:000001", failed[0].GetProperty("lines")[0].GetString());
    }

    [Fact]
    public async Task Linha_recusada_continua_sem_traducao_e_aparece_no_resultado()
    {
        _cli.Cli.TranslationProviderFactory = (_, _) => new RefusingProvider();

        var result = await Run("--files", "abc", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Null(Line("abc", 0).Translation);
        Assert.Equal(TranslationStatus.Untranslated, Line("abc", 0).Status);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal("abc:000001", doc.RootElement.GetProperty("data").GetProperty("refused")[0].GetString());
    }

    [Fact]
    public async Task Cancelar_grava_o_que_ja_voltou_e_sai_com_3()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var calls = 0;
        _cli.Cli.TranslationProviderFactory = (_, _) => new CancelingProvider(() =>
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                cts.Cancel();
            }
        });
        _project.SaveText("grande", Enumerable.Range(1, 60).Select(n => Unit("grande", n, TranslationStatus.Untranslated)).ToList());

        var result = await _cli.RunAsync(
            ["translate", "--project", _projectPath, "--files", "grande", "--batch-size", "20", "--concurrency", "1"], cts.Token);

        Assert.Equal(3, result.ExitCode);
        var saved = _project.LoadText("grande");
        Assert.Equal(20, saved.Count(u => u.Status == TranslationStatus.Machine));
        Assert.All(saved.Take(20), u => Assert.Equal(TranslationStatus.Machine, u.Status));
    }

    [Fact]
    public async Task Glossario_e_guia_de_estilo_do_projeto_chegam_ao_lote_e_files_aceita_lista()
    {
        _project.SaveGlossary([new VNForge.Core.Glossary.GlossaryTerm("原文1", "texto 1", null)]);
        File.WriteAllText(Path.Combine(_projectPath, "style.md"), "Tom informal.");

        await Run("--files", "abc,yst00002");

        Assert.All(_provider.ReceivedBatches, b => Assert.Equal("Tom informal.", b.StyleGuide));
        Assert.All(_provider.ReceivedBatches, b => Assert.Equal("texto 1", Assert.Single(b.Glossary).Target));
        Assert.Equal("[PT] 原文1", Line("yst00002", 0).Translation);
        Assert.Null(Line("yst00001", 0).Translation);
    }

    [Fact]
    public async Task Pasta_que_nao_e_projeto_sai_com_1()
    {
        var result = await _cli.RunAsync(["translate", "--project", _cli.WorkingDirectory], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
    }

    /// <summary>Lança <see cref="ArgumentException"/>, que o runner não repete, para o teste não esperar o backoff.</summary>
    private sealed class FailingProvider : ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct) =>
            throw new ArgumentException("Falha simulada.");
    }

    private sealed class RefusingProvider : ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TranslationResult>>([.. batch.Lines.Select(l => new TranslationResult(l.Id, string.Empty, Refused: true))]);
    }

    /// <summary>Devolve a tradução do lote e chama <paramref name="afterCall"/> antes de responder, para o teste cancelar no meio.</summary>
    private sealed class CancelingProvider(Action afterCall) : ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
        {
            afterCall();
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<TranslationResult> results = [.. batch.Lines.Select(l => new TranslationResult(l.Id, "[PT] " + l.Source))];
            return Task.FromResult(results);
        }
    }
}
