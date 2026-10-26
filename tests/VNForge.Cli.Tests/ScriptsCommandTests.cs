using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class ScriptsCommandTests : IDisposable
{
    private const string JsonOption = "--json";
    private const string ScriptsVerb = "scripts";
    private const string Capitulo1 = "Capítulo 1";
    private const string Capitulo2 = "Capítulo 2";

    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;

    public ScriptsCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig(Path.Combine(_cli.Root, "jogo"), "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        for (var n = 1; n <= 4; n++)
        {
            var key = Key(n);
            _project.SaveText(key, [new TextUnit($"{key}:000001", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated)]);
        }

        _project.SaveScriptNames(ScriptNames.Empty.WithDetected(new Dictionary<string, DetectedScript>(StringComparer.Ordinal)
        {
            [Key(1)] = new("a", "userscript/01_cap1"),
            [Key(2)] = new("b", "userscript/01_cap1"),
            [Key(3)] = new("c", "userscript/02_cap2"),
            [Key(4)] = new("d", "userscript/02_cap2"),
        }));
    }

    public void Dispose() => _cli.Dispose();

    private static string Key(int n) => $"yst{n:D5}";

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync([.. args, "--project", _projectPath], TestContext.Current.CancellationToken);

    [Fact]
    public async Task List_mostra_os_capitulos_com_as_chaves_e_o_json_traz_numero_nome_e_scripts()
    {
        var text = await Run(ScriptsVerb, "list");
        Assert.Equal(0, text.ExitCode);
        Assert.Contains(Capitulo1, text.Output, StringComparison.Ordinal);
        Assert.Contains(Key(3), text.Output, StringComparison.Ordinal);

        var result = await Run(ScriptsVerb, "list", JsonOption);

        using (var doc = JsonDocument.Parse(result.Output))
        {
            var chapters = doc.RootElement.GetProperty("data").GetProperty("chapters").EnumerateArray().ToList();
            Assert.Equal([Capitulo1, Capitulo2], chapters.Select(c => c.GetProperty("name").GetString()));
            Assert.Equal([1, 2], chapters.Select(c => c.GetProperty("number").GetInt32()));
            Assert.Equal([Key(1), Key(2)], chapters[0].GetProperty("scripts").EnumerateArray().Select(k => k.GetString()));
        }
    }

    [Fact]
    public async Task Group_grava_o_grupo_no_scripts_json_e_o_json_devolve_os_capitulos_novos()
    {
        var result = await Run(ScriptsVerb, "group", $"{Key(2)}..{Key(3)}", "Manhã de chuva", JsonOption);

        Assert.Equal(0, result.ExitCode);
        using (var doc = JsonDocument.Parse(result.Output))
        {
            var data = doc.RootElement.GetProperty("data");
            Assert.Equal("grouped", data.GetProperty("action").GetString());
            Assert.Equal("Manhã de chuva", data.GetProperty("group").GetString());
            Assert.Equal([Key(2), Key(3)], data.GetProperty("scripts").EnumerateArray().Select(k => k.GetString()));
            Assert.Equal([Capitulo1, "Manhã de chuva", Capitulo2], data.GetProperty("chapters").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        }

        var names = ProjectFolder.Open(_projectPath).LoadScriptNames();
        Assert.Equal("Manhã de chuva", names.Group(Key(2)));
        Assert.Equal("Manhã de chuva", names.Group(Key(3)));
        Assert.Null(names.Group(Key(1)));
    }

    [Fact]
    public async Task Group_em_texto_diz_quantas_cenas_foram_e_para_qual_capitulo()
    {
        var result = await Run(ScriptsVerb, "group", $"{Key(1)},{Key(2)}", "Prólogo");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("2 cenas", result.Output, StringComparison.Ordinal);
        Assert.Contains("Prólogo", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Group_com_chave_desconhecida_sai_com_1_e_nao_grava()
    {
        var antes = File.ReadAllText(_project.ScriptNamesPath);

        var result = await Run(ScriptsVerb, "group", $"{Key(1)},yst99999", "X");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("yst99999", result.Output + result.Error, StringComparison.Ordinal);
        Assert.Equal(antes, File.ReadAllText(_project.ScriptNamesPath));
    }

    [Fact]
    public async Task Ungroup_devolve_a_cena_ao_capitulo_da_pasta()
    {
        await Run(ScriptsVerb, "group", Key(2), "Solto");

        var result = await Run(ScriptsVerb, "ungroup", Key(2));

        Assert.Equal(0, result.ExitCode);
        Assert.Null(ProjectFolder.Open(_projectPath).LoadScriptNames().Group(Key(2)));
    }

    [Fact]
    public async Task Rename_group_troca_o_nome_pelo_nome_ou_pelo_numero_do_capitulo()
    {
        var byName = await Run(ScriptsVerb, "rename-group", "capítulo 1", "Prólogo", JsonOption);
        var byNumber = await Run(ScriptsVerb, "rename-group", "2", "Tarde");

        Assert.Equal(0, byName.ExitCode);
        Assert.Equal(0, byNumber.ExitCode);
        using (var doc = JsonDocument.Parse(byName.Output))
        {
            var data = doc.RootElement.GetProperty("data");
            Assert.Equal("renamed", data.GetProperty("action").GetString());
            Assert.Equal("Prólogo", data.GetProperty("group").GetString());
        }

        var names = ProjectFolder.Open(_projectPath).LoadScriptNames();
        Assert.Equal(["Prólogo", "Prólogo", "Tarde", "Tarde"], new[] { Key(1), Key(2), Key(3), Key(4) }.Select(names.Group));
    }

    [Fact]
    public async Task Rename_group_de_capitulo_que_nao_existe_sai_com_1()
    {
        var result = await Run(ScriptsVerb, "rename-group", "Epílogo", "Fim");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Epílogo", result.Output + result.Error, StringComparison.Ordinal);
    }
}
