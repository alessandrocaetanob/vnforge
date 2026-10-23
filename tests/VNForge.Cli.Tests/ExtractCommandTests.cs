using System.Security.Cryptography;
using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;

namespace VNForge.Cli.Tests;

public sealed class ExtractCommandTests : IDisposable
{
    private const string YsbinYpf = "ysbin.ypf";
    private const string ExtractVerb = "extract";
    private const string Yst00001 = "yst00001";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();
    private readonly string _game;

    public ExtractCommandTests()
    {
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "MinhaVN.exe"), "MZ");
        File.WriteAllBytes(Path.Combine(_game, YsbinYpf), ScriptArchive());
        File.WriteAllBytes(Path.Combine(_game, "bgm.ypf"), new SyntheticYpfBuilder(500).AddFile("bgm01.ogg", [1, 2, 3]).Build().Bytes);
    }

    public void Dispose() => _cli.Dispose();

    private string ProjectPath => Path.Combine(_cli.WorkingDirectory, "minha-vn-ptbr");

    private static SyntheticYstbBuilder NewScript() => new(Yscm);

    private static byte[] ScriptArchive()
    {
        var first = NewScript().WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        first.AddWord("美咲「おはよう」", 1).AddReturnCode(0, 2);
        first.AddCommand("GOSUB", 3, first.StringExpression("ES.CHAR.NAME"), first.StringExpression("美咲"));

        var second = NewScript().WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        second.AddWord("雨が降っていた。", 1).AddReturnCode(0, 2);
        second.AddCommand("GOSUB", 3, second.StringExpression("ES.SEL.SET"), second.StringExpression("はい"), second.StringExpression("いいえ"));

        var list = new SyntheticYstlBuilder().WithVersion(500).AddScript(@"userscript\global.yst").AddScript(@"userscript\01_prologo.yst").Build();
        return new SyntheticYpfBuilder(500)
            .AddFile("ysc.ybn", Yscm.Bytes)
            .AddFile("yst_list.ybn", list.Bytes)
            .AddFile("yst00001.ybn", first.Build().Bytes)
            .AddFile("yst00002.ybn", second.Build().Bytes)
            .Build().Bytes;
    }

    private async Task InitProject()
    {
        var init = await _cli.RunAsync(["init", "minha-vn-ptbr", "--game", _game], TestContext.Current.CancellationToken);
        Assert.Equal(0, init.ExitCode);
    }

    private Task<RunResult> Extract(params string[] args) =>
        _cli.RunAsync([ExtractVerb, .. args, "--project", ProjectPath], TestContext.Current.CancellationToken);

    private static Dictionary<string, string> HashTree(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(directory, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    [Fact]
    public async Task Resumo_mostra_scripts_e_linhas_por_kind_como_no_doc_03()
    {
        await InitProject();

        var result = await Extract();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("Extraídos 2 scripts, 5 linhas (1 diálogo, 1 narração, 2 escolhas, 1 nome)", lines[^1]);
        Assert.StartsWith("Descompactado: ysbin.ypf (4 arquivos) em ", lines[0]);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task Grava_text_json_por_script_e_descompacta_so_o_pacote_de_scripts_em_cache()
    {
        await InitProject();

        await Extract();

        var project = ProjectFolder.Open(ProjectPath);
        Assert.Equal([Yst00001, "yst00002"], project.LoadAllText().Keys);
        Assert.Equal(["美咲", "おはよう"], [project.LoadText(Yst00001)[0].Speaker, project.LoadText(Yst00001)[0].Source]);
        Assert.Equal(
            [TextUnitKind.Narration, TextUnitKind.Choice, TextUnitKind.Choice],
            project.LoadText("yst00002").Select(u => u.Kind));
        Assert.True(File.Exists(Path.Combine(ProjectPath, ".cache", "game", YsbinYpf, "yst00001.ybn")));
        Assert.False(Directory.Exists(Path.Combine(ProjectPath, ".cache", "game", "bgm.ypf")));
    }

    [Fact]
    public async Task Nao_altera_a_pasta_do_jogo()
    {
        await InitProject();
        var before = HashTree(_game);
        var entriesBefore = Directory.EnumerateFileSystemEntries(_game, "*", SearchOption.AllDirectories).Order().ToArray();

        var result = await Extract();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(before, HashTree(_game));
        Assert.Equal(entriesBefore, Directory.EnumerateFileSystemEntries(_game, "*", SearchOption.AllDirectories).Order().ToArray());
    }

    [Fact]
    public async Task Json_traz_o_resumo_no_envelope()
    {
        await InitProject();

        var result = await Extract("--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
        var data = root.GetProperty("data");
        Assert.Equal(2, data.GetProperty("scripts").GetInt32());
        Assert.Equal(5, data.GetProperty("lines").GetInt32());
        var kinds = data.GetProperty("kinds");
        Assert.Equal(1, kinds.GetProperty("dialogue").GetInt32());
        Assert.Equal(1, kinds.GetProperty("narration").GetInt32());
        Assert.Equal(2, kinds.GetProperty("choice").GetInt32());
        Assert.Equal(1, kinds.GetProperty("name").GetInt32());
        Assert.Equal(0, kinds.GetProperty("system").GetInt32());
        Assert.Equal(0, kinds.GetProperty("other").GetInt32());
        var texts = data.GetProperty("texts");
        Assert.Equal(Yst00001, texts[0].GetProperty("key").GetString());
        Assert.Equal("01_prologo", texts[0].GetProperty("name").GetString());
        Assert.Equal(2, texts[0].GetProperty("lines").GetInt32());
        Assert.False(texts[1].TryGetProperty("name", out _));
        var archive = data.GetProperty("archives")[0];
        Assert.Equal(YsbinYpf, archive.GetProperty("path").GetString());
        Assert.Equal(4, archive.GetProperty("files").GetInt32());
        Assert.Equal(0, data.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public async Task Extrair_de_novo_mantem_as_traducoes_e_resume_a_re_extracao()
    {
        await InitProject();
        await Extract();
        var project = ProjectFolder.Open(ProjectPath);
        var edited = project.LoadText(Yst00001).Select(u => u with { Translation = "Bom dia", Status = TranslationStatus.Translated }).ToList();
        project.SaveText(Yst00001, edited);
        var before = HashTree(project.TextDirectory);

        var result = await Extract();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Re-extração: 5 mantidas, 0 desatualizadas", result.Output);
        Assert.Equal(before, HashTree(project.TextDirectory));
        Assert.Equal("Bom dia", project.LoadText(Yst00001)[0].Translation);
    }

    [Fact]
    public async Task Extrair_de_novo_em_json_traz_a_contagem_da_re_extracao()
    {
        await InitProject();
        var first = await Extract("--json");
        using var firstDocument = JsonDocument.Parse(first.Output);
        Assert.False(firstDocument.RootElement.GetProperty("data").TryGetProperty("reextraction", out var none) && none.ValueKind != JsonValueKind.Null);

        var result = await Extract("--json");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var reextraction = document.RootElement.GetProperty("data").GetProperty("reextraction");
        Assert.Equal(0, reextraction.GetProperty("outdated").GetInt32());
        Assert.Equal(0, reextraction.GetProperty("removed").GetInt32());
        Assert.True(reextraction.GetProperty("kept").GetInt32() > 0);
    }

    [Fact]
    public async Task Sem_project_usa_a_pasta_atual()
    {
        var init = await _cli.RunAsync(["init", ".", "--game", _game], TestContext.Current.CancellationToken);
        Assert.Equal(0, init.ExitCode);

        var result = await _cli.RunAsync([ExtractVerb], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Extraídos 2 scripts", result.Output);
        Assert.True(File.Exists(Path.Combine(_cli.WorkingDirectory, "text", "yst00001.json")));
    }

    [Fact]
    public async Task Pasta_que_nao_e_projeto_gera_erro_em_portugues()
    {
        var result = await _cli.RunAsync([ExtractVerb, "--project", _cli.WorkingDirectory], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("vnforge.json não encontrado", result.Error);
    }

    [Fact]
    public async Task Jogo_sem_pacote_de_scripts_gera_erro_em_portugues_e_nao_cria_text()
    {
        await InitProject();
        File.Delete(Path.Combine(_game, YsbinYpf));

        var result = await Extract();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Nenhum pacote YPF com scripts", result.Error);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(ProjectPath, "text")));
    }

    [Fact]
    public async Task Help_do_extract_esta_em_portugues()
    {
        var result = await _cli.RunAsync([ExtractVerb, "--help"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("vnf extract", result.Output);
        Assert.Contains(".cache/", result.Output);
    }
}
