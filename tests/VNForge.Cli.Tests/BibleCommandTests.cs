using System.Text.Json;
using VNForge.AI.Bible;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.Core.Bible;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class BibleCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;
    private readonly FakeBibleProvider _provider = new();
    private string? _requestedModel;

    public BibleCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = AiTestProject.Create(_cli.Root, _projectPath);
        _project.SaveText("yst1", [Line("yst1", 1, "おはよう"), Line("yst1", 2, "遅いぞ")]);
        _project.SaveText("yst2", [Line("yst2", 1, "ただいま")]);
        _project.SaveText("yst3", [Line("yst3", 1, "さようなら")]);
        _project.SaveScriptNames(ScriptNames.Empty.WithDetected(new Dictionary<string, DetectedScript>
        {
            ["yst1"] = new("manha", "userscript/01_cap1"),
            ["yst2"] = new("tarde", "userscript/01_cap1"),
            ["yst3"] = new("noite", "userscript/02_cap2"),
        }).WithGroup("yst1", "Manhã de chuva").WithGroup("yst2", "Manhã de chuva").WithGroup("yst3", "Festival"));
        _cli.Cli.BibleProviderFactory = (model, _) =>
        {
            _requestedModel = model;
            return _provider;
        };
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Line(string key, int n, string source) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, "美咲", source, null, TranslationStatus.Untranslated);

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync(["bible", .. args, "--project", _projectPath], TestContext.Current.CancellationToken);

    [Fact]
    public async Task Dry_run_mostra_capitulos_cenas_e_o_preco_sem_chamar_a_IA()
    {
        var result = await Run("generate", "--dry-run");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("2 capítulos", result.Output);
        Assert.Contains("3 cenas", result.Output);
        Assert.Contains("R$ ", result.Output);
        Assert.Contains("Nada foi enviado", result.Output);
        Assert.Empty(_provider.SceneCalls);
        Assert.False(File.Exists(_project.BiblePath));
    }

    [Fact]
    public async Task Dry_run_em_json_traz_o_custo_em_dolar_e_em_reais()
    {
        var result = await Run("generate", "--dry-run", "--json");

        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("dryRun").GetBoolean());
        Assert.Equal(2, data.GetProperty("chapters").GetInt32());
        Assert.Equal(3, data.GetProperty("scenes").GetInt32());
        Assert.True(data.GetProperty("estimate").GetProperty("dollars").GetDecimal() > 0);
        Assert.True(data.GetProperty("estimate").GetProperty("brl").GetDecimal() > 0);
        Assert.True(data.GetProperty("estimate").GetProperty("cacheWriteTokens").GetInt64() > 0);
    }

    [Fact]
    public async Task Sem_yes_pergunta_o_preco_e_a_resposta_negativa_cancela()
    {
        _cli.SetInput("n\n");

        var result = await Run("generate");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("custa cerca de R$ ", result.Error);
        Assert.Contains("nenhuma cena foi enviada", result.Output);
        Assert.Empty(_provider.SceneCalls);
        Assert.False(File.Exists(_project.BiblePath));
    }

    [Fact]
    public async Task Confirmando_gera_a_biblia_e_grava_o_bible_json()
    {
        _cli.SetInput("s\n");

        var result = await Run("generate");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("3 cenas escritas em 2 capítulos", result.Output);
        Assert.Equal(["yst1", "yst2", "yst3"], _project.LoadBible().Scenes.Select(s => s.Key));
        Assert.Equal(VNForge.AI.Claude.ClaudeModels.Opus, _requestedModel);
        Assert.NotEmpty(_project.LoadBible().Synopsis);
    }

    [Fact]
    public async Task Bible_json_ilegivel_para_o_generate_antes_de_pagar_e_nao_toca_o_backup()
    {
        _project.SaveBible(GameBible.From([new BibleScene("yst1") { Summary = "Boa" }]));
        _project.KeepPreviousBible();
        var backup = File.ReadAllText(_project.PreviousBiblePath);
        File.WriteAllText(_project.BiblePath, "{ isto não é json");
        _cli.SetInput("s\n");

        var result = await Run("generate");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("bible.json", result.Error + result.Output, StringComparison.Ordinal);
        Assert.Contains("inválido", result.Error + result.Output, StringComparison.Ordinal);
        Assert.Empty(_provider.SceneCalls);
        Assert.Equal(backup, File.ReadAllText(_project.PreviousBiblePath));
    }

    [Fact]
    public async Task Estimativa_maior_que_o_restante_do_limite_recusa_antes_de_gastar()
    {
        Directory.CreateDirectory(_cli.UserConfigDirectory);
        File.WriteAllText(Path.Combine(_cli.UserConfigDirectory, AppBudgetFile.FileName), """{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "spendLimit": 0.01 }""");
        _cli.SetInput("s\n");

        var result = await Run("generate");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("passaria do limite", result.Output, StringComparison.Ordinal);
        Assert.Empty(_provider.SceneCalls);
        Assert.False(File.Exists(_project.BiblePath));
    }

    [Fact]
    public async Task Estimativa_que_cabe_no_restante_do_limite_gera_normalmente()
    {
        Directory.CreateDirectory(_cli.UserConfigDirectory);
        File.WriteAllText(Path.Combine(_cli.UserConfigDirectory, AppBudgetFile.FileName), """{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "spendLimit": 500 }""");
        _cli.SetInput("s\n");

        var result = await Run("generate");

        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(_provider.SceneCalls);
    }

    [Fact]
    public async Task O_modelo_vem_da_opcao()
    {
        var result = await Run("generate", "--yes", "--model", "claude-sonnet-5-5");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("claude-sonnet-5-5", _requestedModel);
        Assert.Equal("claude-sonnet-5-5", _project.LoadBible().Generation!.Model);
    }

    [Fact]
    public async Task O_nome_da_cena_da_biblia_aparece_nas_listas_do_projeto()
    {
        await Run("generate", "--yes");

        var status = await _cli.RunAsync(["status", "--project", _projectPath, "--json"], TestContext.Current.CancellationToken);

        Assert.Contains("Cena manha", status.Output);
    }

    [Fact]
    public async Task Um_capitulo_so_pelo_numero_ou_pelo_nome_e_sem_sinopse()
    {
        var byNumber = await Run("generate", "--yes", "--chapter", "2");
        var calls = _provider.SceneCalls.Select(c => c.Chapter.Name).ToList();

        Assert.Equal(0, byNumber.ExitCode);
        Assert.Equal(["Festival"], calls);
        Assert.Empty(_provider.OverviewCalls);

        var byName = await Run("generate", "--yes", "--chapter", "manhã de chuva");
        Assert.Equal(0, byName.ExitCode);
        Assert.Equal(["yst1", "yst2", "yst3"], _project.LoadBible().Scenes.Select(s => s.Key));
    }

    [Fact]
    public async Task Capitulo_que_nao_existe_diz_quais_existem()
    {
        var result = await Run("generate", "--yes", "--chapter", "9");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Capítulo '9' não encontrado", result.Error);
        Assert.Contains("Manhã de chuva", result.Error);
    }

    [Fact]
    public async Task Capitulo_que_falha_sai_com_erro_e_o_resto_fica_gravado()
    {
        var failing = new FakeBibleProvider((chapter, key) => chapter.Name == "Festival" ? throw new InvalidOperationException("falha simulada") : new BibleScene(key) { Name = "N", Summary = "R" });
        _cli.Cli.BibleProviderFactory = (_, _) => failing;

        var result = await Run("generate", "--yes");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Festival", result.Output);
        Assert.Contains("falha simulada", result.Output);
        Assert.Equal(["yst1", "yst2"], _project.LoadBible().Scenes.Select(s => s.Key));
    }

    [Fact]
    public async Task Show_lista_as_cenas_com_nome_e_quais_campos_sao_seus()
    {
        _project.SaveBible(GameBible.From(
        [
            new BibleScene("yst1") { Name = "Atraso no portão", Summary = "Kenta e Ren chegam atrasados." }.EditSummary("Kenta e Ren chegam atrasados."),
            new BibleScene("yst2"),
        ]));

        var result = await Run("show");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("yst1", result.Output);
        Assert.Contains("Atraso no portão", result.Output);
        Assert.Contains("Kenta e Ren chegam atrasados.", result.Output);
        Assert.Contains("sem resumo", result.Output);
    }

    [Fact]
    public async Task Show_de_uma_cena_traz_todos_os_campos()
    {
        _project.SaveBible(GameBible.From(
        [
            new BibleScene("yst1") { Name = "Atraso", Summary = "Resumo.", Characters = ["Kenta"], Tone = ["seco"], Jokes = ["o guarda-chuva"], Relations = [new BibleRelation("Kenta e Ren", "Amigos")] }.WithEdited(BibleField.Tone, true),
        ]));

        var result = await Run("show", "yst1");

        Assert.Contains("Atraso", result.Output);
        Assert.Contains("Resumo.", result.Output);
        Assert.Contains("Kenta", result.Output);
        Assert.Contains("seco", result.Output);
        Assert.Contains("(sua)", result.Output);
        Assert.Contains("o guarda-chuva", result.Output);
        Assert.Contains("Kenta e Ren", result.Output);
    }

    [Fact]
    public async Task Show_em_json_devolve_a_cena_com_os_campos_editados()
    {
        _project.SaveBible(GameBible.From([new BibleScene("yst1") { Summary = "Resumo." }.WithEdited(BibleField.Summary, true)]));

        var result = await Run("show", "yst1", "--json");

        using var doc = JsonDocument.Parse(result.Output);
        var scene = doc.RootElement.GetProperty("data").GetProperty("scene");
        Assert.Equal("yst1", scene.GetProperty("key").GetString());
        Assert.Equal("summary", scene.GetProperty("edited")[0].GetString());
    }

    [Fact]
    public async Task Show_de_cena_que_nao_esta_na_biblia_e_erro()
    {
        var result = await Run("show", "yst9");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("yst9", result.Error);
    }

    [Fact]
    public async Task Edit_grava_o_campo_como_seu_e_a_proxima_geracao_o_respeita()
    {
        await Run("generate", "--yes");

        var edit = await Run("edit", "yst1", "--summary", "Meu resumo.", "--tone", "seco, tenso", "--relation", "Kenta e Ren=Amigos");
        await Run("generate", "--yes");

        Assert.Equal(0, edit.ExitCode);
        Assert.Contains("yst1", edit.Output);
        var scene = _project.LoadBible().Scene("yst1")!;
        Assert.Equal("Meu resumo.", scene.Summary);
        Assert.Equal(["seco", "tenso"], scene.Tone);
        Assert.Equal(new BibleRelation("Kenta e Ren", "Amigos"), Assert.Single(scene.Relations));
        Assert.Equal([BibleField.Summary, BibleField.Tone, BibleField.Relations], scene.Edited);
    }

    [Fact]
    public async Task Edit_do_jogo_troca_a_sinopse()
    {
        var result = await Run("edit", "--game", "--synopsis", "Uma cidade onde sempre chove.");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Uma cidade onde sempre chove.", _project.LoadBible().Synopsis);
        Assert.True(_project.LoadBible().SynopsisEdited);
    }

    [Fact]
    public async Task Edit_sem_dizer_o_que_mudar_e_erro_de_uso()
    {
        var result = await Run("edit", "yst1");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--summary", result.Error);
    }

    [Fact]
    public async Task Edit_cria_a_cena_se_o_script_existe_no_projeto_e_recusa_chave_inventada()
    {
        var ok = await Run("edit", "yst2", "--name", "Volta para casa");
        var wrong = await Run("edit", "yst77", "--name", "x");

        Assert.Equal(0, ok.ExitCode);
        Assert.Equal("Volta para casa", _project.LoadBible().SceneName("yst2"));
        Assert.Equal(1, wrong.ExitCode);
        Assert.Contains("yst77", wrong.Error);
    }

    [Fact]
    public async Task Export_escreve_o_markdown_no_arquivo_ou_na_saida()
    {
        _project.SaveBible(GameBible.From([new BibleScene("yst1") { Name = "Atraso", Summary = "Resumo." }]) with { Synopsis = "Sinopse." });
        var target = Path.Combine(_cli.Root, "biblia.md");

        var toFile = await Run("export", "--out", target);
        var toOutput = await Run("export");

        Assert.Equal(0, toFile.ExitCode);
        Assert.Contains("## Sinopse", File.ReadAllText(target));
        Assert.StartsWith("# Bíblia de Minha VN", toOutput.Output);
        Assert.Contains("### yst1 · Atraso", toOutput.Output);
    }

    [Fact]
    public async Task Undo_volta_a_biblia_de_antes_da_ultima_geracao()
    {
        _project.SaveBible(GameBible.From([new BibleScene("yst1") { Summary = "Antes" }]));
        await Run("generate", "--yes");

        var undo = await Run("undo");

        Assert.Equal(0, undo.ExitCode);
        Assert.Equal("Antes", _project.LoadBible().Scene("yst1")!.Summary);
        Assert.Equal(["yst1"], _project.LoadBible().Scenes.Select(s => s.Key));
    }

    [Fact]
    public async Task Undo_sem_geracao_anterior_diz_que_nao_ha_a_que_voltar()
    {
        var result = await Run("undo");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("não há bíblia anterior", result.Error);
    }

    [Fact]
    public async Task O_translate_manda_o_resumo_da_cena_em_cada_pedido()
    {
        _project.SaveBible(GameBible.From([new BibleScene("yst1") { Name = "Atraso", Summary = "Kenta e Ren chegam atrasados." }]) with { Synopsis = "Sinopse do jogo." });
        var translator = new FakeTranslationProvider();
        _cli.Cli.TranslationProviderFactory = (_, _) => translator;

        var result = await _cli.RunAsync(["translate", "--project", _projectPath, "--yes"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        var withScene = translator.ReceivedBatches.Where(b => b.Scenes.Count > 0).ToList();
        Assert.Contains(withScene, b => b.Scenes[0].Summary == "Kenta e Ren chegam atrasados." && b.Lines.Any(l => l.Id.StartsWith("yst1:", StringComparison.Ordinal)));
        Assert.All(translator.ReceivedBatches, b => Assert.Equal("Sinopse do jogo.", b.Synopsis));
    }

    [Fact]
    public async Task Biblia_ilegivel_nao_trava_o_translate_e_avisa()
    {
        File.WriteAllText(_project.BiblePath, "{ quebrado");
        _cli.Cli.TranslationProviderFactory = (_, _) => new FakeTranslationProvider();

        var result = await _cli.RunAsync(["translate", "--project", _projectPath, "--yes"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("bible.json", result.Error);
    }
}
