using System.Text.Json;
using VNForge.Core.Dictionaries;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class DictionaryCommandTests : IDisposable
{
    private const string Dict = "dict";
    private const string JsonOption = "--json";

    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;

    public DictionaryCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig(Path.Combine(_cli.Root, "jogo"), "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
    }

    public void Dispose() => _cli.Dispose();

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync([.. args, "--project", _projectPath], TestContext.Current.CancellationToken);

    [Fact]
    public async Task Add_pos_grava_a_regra_com_as_condicoes_no_formatting_json()
    {
        var result = await Run(Dict, "add", "pos", "você", "tu", "--speaker", "Ren", "--scene", "cap1", "--contains", "先生");

        Assert.Equal(0, result.ExitCode);
        var rule = Assert.Single(_project.LoadFormatting().Dictionary.Post);
        Assert.Equal("você", rule.Pattern);
        Assert.Equal("tu", rule.With);
        Assert.Equal(
            [
                new DictionaryCondition(DictionaryConditionKind.Speaker, "Ren"),
                new DictionaryCondition(DictionaryConditionKind.Scene, "cap1"),
                new DictionaryCondition(DictionaryConditionKind.SourceContains, "先生"),
            ],
            rule.Conditions);
        Assert.Empty(_project.LoadFormatting().Dictionary.Pre);
    }

    [Fact]
    public async Task Add_pre_regex_com_json_devolve_a_regra()
    {
        var result = await Run(Dict, "add", "pre", "(ぼ、)+ぼく", "ぼく", "--regex", JsonOption);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("added", data.GetProperty("action").GetString());
        Assert.Equal("pre", data.GetProperty("side").GetString());
        Assert.True(data.GetProperty("rule").GetProperty("regex").GetBoolean());
        Assert.True(Assert.Single(_project.LoadFormatting().Dictionary.Pre).Regex);
    }

    [Fact]
    public async Task Add_com_regex_invalida_ou_lado_desconhecido_falha_sem_gravar()
    {
        var badRegex = await Run(Dict, "add", "pos", "(", "x", "--regex");
        var badSide = await Run(Dict, "add", "meio", "a", "b");

        Assert.Equal(1, badRegex.ExitCode);
        Assert.Equal(1, badSide.ExitCode);
        Assert.True(_project.LoadFormatting().Dictionary.IsEmpty);
    }

    [Fact]
    public async Task Remove_apaga_pela_posicao_e_posicao_inexistente_falha()
    {
        await Run(Dict, "add", "pos", "a", "b");
        await Run(Dict, "add", "pos", "c", "d");

        var missing = await Run(Dict, "remove", "pos", "5");
        var removed = await Run(Dict, "remove", "pos", "1");

        Assert.Equal(1, missing.ExitCode);
        Assert.Equal(0, removed.ExitCode);
        Assert.Equal("c", Assert.Single(_project.LoadFormatting().Dictionary.Post).Pattern);
    }

    [Fact]
    public async Task Disable_e_enable_ligam_e_desligam_sem_apagar_a_regra()
    {
        await Run(Dict, "add", "pre", "a", "b");

        await Run(Dict, "disable", "pre", "1");
        Assert.False(Assert.Single(_project.LoadFormatting().Dictionary.Pre).Enabled);

        await Run(Dict, "enable", "pre", "1");
        Assert.True(Assert.Single(_project.LoadFormatting().Dictionary.Pre).Enabled);
    }

    [Fact]
    public async Task List_json_conta_as_linhas_que_trocam_e_as_que_a_condicao_impede()
    {
        _project.SaveText("cap1",
        [
            new TextUnit("cap1:000001", TextUnitKind.Dialogue, "蓮", "原", "você vem?", TranslationStatus.Translated),
            new TextUnit("cap1:000002", TextUnitKind.Dialogue, "葵", "原", "você sabe.", TranslationStatus.Translated),
            new TextUnit("cap1:000003", TextUnitKind.Dialogue, "蓮", "ぼ、ぼく", null, TranslationStatus.Untranslated),
        ]);
        await Run(Dict, "add", "pos", "você", "tu", "--speaker", "蓮");
        await Run(Dict, "add", "pre", "ぼ、ぼく", "ぼく");

        var result = await Run(Dict, "list", JsonOption);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var rules = doc.RootElement.GetProperty("data").GetProperty("rules").EnumerateArray().ToList();
        var pre = rules.Single(r => r.GetProperty("side").GetString() == "pre");
        var post = rules.Single(r => r.GetProperty("side").GetString() == "pos");
        Assert.Equal(1, pre.GetProperty("applies").GetInt32());
        Assert.Equal(1, post.GetProperty("applies").GetInt32());
        Assert.Equal(1, post.GetProperty("blocked").GetInt32());
    }

    [Fact]
    public async Task List_sem_regras_diz_que_esta_vazio()
    {
        var result = await Run(Dict, "list");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Nenhuma regra", result.Output);
    }
}
