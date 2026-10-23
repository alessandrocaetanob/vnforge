using System.Security.Cryptography;
using System.Text.Json;
using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;
using VNForge.Engines.YuRis.Text;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Cli.Tests;

public sealed class BuildCommandTests : IDisposable
{
    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();
    private readonly string _game;
    private readonly byte[] _scriptsYpf;
    private readonly byte[] _extraYpf;

    public BuildCommandTests()
    {
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "MinhaVN.exe"), "MZ");
        _scriptsYpf = ScriptArchive();
        _extraYpf = ExtraArchive();
        File.WriteAllBytes(Path.Combine(_game, "ysbin.ypf"), _scriptsYpf);
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

    private static byte[] ExtraArchive()
    {
        var third = NewScript().WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        third.AddWord("夜になった。", 1).AddReturnCode(0, 2);
        return new SyntheticYpfBuilder(500).AddFile("yst00003.ybn", third.Build().Bytes).Build().Bytes;
    }

    private async Task InitAndExtract(bool withExtraArchive = false)
    {
        if (withExtraArchive)
        {
            Directory.CreateDirectory(Path.Combine(_game, "pac"));
            File.WriteAllBytes(Path.Combine(_game, "pac", "extra.ypf"), _extraYpf);
        }

        var init = await _cli.RunAsync(["init", "minha-vn-ptbr", "--game", _game], TestContext.Current.CancellationToken);
        Assert.Equal(0, init.ExitCode);
        var extract = await _cli.RunAsync(["extract", "--project", ProjectPath], TestContext.Current.CancellationToken);
        Assert.Equal(0, extract.ExitCode);
    }

    private Task<RunResult> Build(params string[] args) =>
        _cli.RunAsync(["build", .. args, "--project", ProjectPath], TestContext.Current.CancellationToken);

    private void Translate(string key, Func<int, string> translation)
    {
        var project = ProjectFolder.Open(ProjectPath);
        var units = project.LoadText(key).Select((u, i) => u with { Translation = translation(i), Status = TranslationStatus.Translated }).ToList();
        project.SaveText(key, units);
    }

    private static Dictionary<string, string> HashTree(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(directory, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    private static Dictionary<string, byte[]> ReadEntries(string ypfPath)
    {
        using var stream = File.OpenRead(ypfPath);
        using var archive = (YpfArchive)new YpfArchiveFormat().Open(stream);
        return archive.Entries.ToDictionary(e => e.Path, e =>
        {
            using var content = archive.OpenEntry(e);
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            return buffer.ToArray();
        });
    }

    private static IReadOnlyList<TextUnit> Reextract(string name, byte[] bytes)
    {
        var commands = YscmReader.Read(Yscm.Bytes, "ysc.ybn");
        var file = new ScriptFile(name, bytes);
        var script = YstbReader.Read(bytes, name, commands);
        return YstbTextExtractor.Extract(file, script, commands).Units;
    }

    [Fact]
    public async Task Sem_traducao_o_ysbin_ypf_do_build_e_identico_ao_original()
    {
        await InitAndExtract();

        var result = await Build();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        var built = Path.Combine(ProjectPath, "build", "ysbin.ypf");
        Assert.Equal(_scriptsYpf, File.ReadAllBytes(built));
        Assert.Equal(["ysbin.ypf"], Directory.EnumerateFiles(Path.Combine(ProjectPath, "build"), "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(Path.Combine(ProjectPath, "build"), f)));
    }

    [Fact]
    public async Task Resumo_conta_scripts_linhas_e_pacotes()
    {
        await InitAndExtract();
        Translate("yst00001", i => i == 0 ? "Good morning" : "Mika");

        var result = await Build();

        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.StartsWith("Gravado: ", lines[0]);
        Assert.EndsWith("ysbin.ypf (1 script alterado)", lines[0]);
        Assert.Equal("Reconstruídos 1 script (2 linhas trocadas); 1 pacote gravado em build/", lines[^1]);
    }

    [Fact]
    public async Task Sem_traducao_o_resumo_diz_que_o_pacote_saiu_identico()
    {
        await InitAndExtract();

        var result = await Build();

        Assert.Contains("(sem alterações, idêntico ao original)", result.Output);
        Assert.Contains("Reconstruídos 0 scripts (0 linhas trocadas); 1 pacote gravado em build/", result.Output);
    }

    [Fact]
    public async Task Nao_altera_a_pasta_do_jogo()
    {
        var before = HashTree(_game);
        var entriesBefore = Directory.EnumerateFileSystemEntries(_game, "*", SearchOption.AllDirectories).Order().ToArray();
        await InitAndExtract();
        Translate("yst00001", _ => "Hello");
        Translate("yst00002", _ => "Rain");

        var result = await Build();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(before, HashTree(_game));
        Assert.Equal(entriesBefore, Directory.EnumerateFileSystemEntries(_game, "*", SearchOption.AllDirectories).Order().ToArray());
    }

    [Fact]
    public async Task Nao_altera_o_cache_descompactado()
    {
        await InitAndExtract();
        Translate("yst00001", _ => "Hello");
        var cache = HashTree(Path.Combine(ProjectPath, ".cache", "game"));

        await Build();

        Assert.Equal(cache, HashTree(Path.Combine(ProjectPath, ".cache", "game")));
    }

    [Fact]
    public async Task Linha_traduzida_em_ascii_reabre_no_ypf_e_reextrai_com_a_traducao()
    {
        await InitAndExtract();
        Translate("yst00001", i => i == 0 ? "Good morning" : "Mika");

        var result = await Build();

        Assert.Equal(0, result.ExitCode);
        var original = ReadEntries(Path.Combine(_game, "ysbin.ypf"));
        var built = ReadEntries(Path.Combine(ProjectPath, "build", "ysbin.ypf"));
        Assert.Equal(original.Keys.Order(), built.Keys.Order());
        var units = Reextract("yst00001.ybn", built["yst00001.ybn"]);
        Assert.Equal(["Good morning", "Mika"], units.Select(u => u.Source));
        Assert.Equal(Reextract("yst00001.ybn", original["yst00001.ybn"]).Select(u => u.Id), units.Select(u => u.Id));
        Assert.NotEqual(original["yst00001.ybn"], built["yst00001.ybn"]);
        foreach (var other in original.Keys.Where(k => k != "yst00001.ybn"))
        {
            Assert.Equal(original[other], built[other]);
        }
    }

    [Fact]
    public async Task Pacote_em_subpasta_mantem_o_caminho_e_o_sem_mudanca_sai_identico()
    {
        await InitAndExtract(withExtraArchive: true);
        Translate("yst00003", _ => "Night fell.");

        var result = await Build("--json");

        Assert.Equal(0, result.ExitCode);
        var build = Path.Combine(ProjectPath, "build");
        Assert.Equal(_scriptsYpf, File.ReadAllBytes(Path.Combine(build, "ysbin.ypf")));
        var extra = ReadEntries(Path.Combine(build, "pac", "extra.ypf"));
        Assert.Equal("Night fell.", Assert.Single(Reextract("yst00003.ybn", extra["yst00003.ybn"])).Source);
        using var document = JsonDocument.Parse(result.Output);
        var archives = document.RootElement.GetProperty("data").GetProperty("archives");
        Assert.Equal(["pac/extra.ypf", "ysbin.ypf"], archives.EnumerateArray().Select(a => a.GetProperty("path").GetString()!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Json_traz_o_resumo_no_envelope()
    {
        await InitAndExtract();
        Translate("yst00002", _ => "Rain");

        var result = await Build("--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
        var data = root.GetProperty("data");
        Assert.Equal(1, data.GetProperty("scripts").GetInt32());
        Assert.Equal(2, data.GetProperty("totalScripts").GetInt32());
        Assert.Equal(3, data.GetProperty("lines").GetInt32());
        var rebuilt = Assert.Single(data.GetProperty("rebuilt").EnumerateArray());
        Assert.Equal("yst00002", rebuilt.GetProperty("key").GetString());
        Assert.Equal("yst00002.ybn", rebuilt.GetProperty("name").GetString());
        var archive = Assert.Single(data.GetProperty("archives").EnumerateArray());
        Assert.Equal("ysbin.ypf", archive.GetProperty("path").GetString());
        Assert.Equal(1, archive.GetProperty("replacedFiles").GetInt32());
        Assert.Equal(0, data.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public async Task Caractere_fora_do_shift_jis_aborta_citando_o_id_e_nao_toca_no_build_anterior()
    {
        await InitAndExtract();
        var first = await Build();
        Assert.Equal(0, first.ExitCode);
        var build = Path.Combine(ProjectPath, "build");
        var before = HashTree(build);
        Translate("yst00001", i => i == 0 ? "Bom dia 😀" : "Mika");

        var result = await Build();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("yst00001.ybn", result.Error);
        Assert.Contains("yst00001:000000", result.Error);
        Assert.Equal(before, HashTree(build));
        Assert.False(Directory.Exists(build + ".tmp"));
    }

    [Fact]
    public async Task Marcador_invalido_aborta_citando_o_id_sem_criar_o_build()
    {
        await InitAndExtract();
        Translate("yst00001", i => i == 0 ? "Bom {XYZ:1} dia" : "Mika");

        var result = await Build();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("yst00001:000000", result.Error);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(ProjectPath, "build"), "*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(ProjectPath, "build.tmp")));
    }

    [Fact]
    public async Task Texto_que_nao_e_mais_o_do_script_aborta_citando_o_id()
    {
        await InitAndExtract();
        var project = ProjectFolder.Open(ProjectPath);
        var units = project.LoadText("yst00001").Select((u, i) => i == 0 ? u with { Source = "outra coisa", Translation = "Hi", Status = TranslationStatus.Translated } : u).ToList();
        project.SaveText("yst00001", units);

        var result = await Build();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("yst00001:000000", result.Error);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(ProjectPath, "build"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Erro_em_json_sai_no_envelope()
    {
        await InitAndExtract();
        Translate("yst00001", _ => "😀");

        var result = await Build("--json");

        Assert.Equal(1, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("yst00001:000000", document.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Jogo_que_mudou_depois_da_extracao_e_recusado()
    {
        await InitAndExtract();
        Translate("yst00001", _ => "Hello");
        var changed = NewScript().WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        changed.AddWord("まったく違う", 1).AddReturnCode(0, 2);
        File.WriteAllBytes(
            Path.Combine(_game, "ysbin.ypf"),
            new SyntheticYpfBuilder(500).AddFile("ysc.ybn", Yscm.Bytes).AddFile("yst00001.ybn", changed.Build().Bytes).Build().Bytes);

        var result = await Build();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("o jogo mudou depois da extração", result.Error);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(ProjectPath, "build"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Build_anterior_e_substituido_por_inteiro()
    {
        await InitAndExtract();
        var stale = Path.Combine(ProjectPath, "build", "sobra.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "x");

        var result = await Build();

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(stale));
    }

    [Fact]
    public async Task Sem_extract_pede_para_rodar_vnf_extract()
    {
        var init = await _cli.RunAsync(["init", "minha-vn-ptbr", "--game", _game], TestContext.Current.CancellationToken);
        Assert.Equal(0, init.ExitCode);

        var result = await Build();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Rode vnf extract", result.Error);
    }

    [Fact]
    public async Task Texto_sem_script_correspondente_vira_aviso()
    {
        await InitAndExtract();
        var project = ProjectFolder.Open(ProjectPath);
        project.SaveText("yst99999", [new TextUnit("yst99999:000000", TextUnitKind.Dialogue, null, "x", "y", TranslationStatus.Translated)]);

        var result = await Build();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Aviso: text/yst99999.json não corresponde a nenhum script", result.Output);
    }

    [Fact]
    public async Task Pasta_que_nao_e_projeto_gera_erro_em_portugues()
    {
        var result = await _cli.RunAsync(["build", "--project", _cli.WorkingDirectory], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("vnforge.json não encontrado", result.Error);
    }

    [Fact]
    public async Task Inject_grava_o_build_no_jogo_e_restore_devolve_o_original()
    {
        await InitAndExtract();
        Translate("yst00001", i => i == 0 ? "Good morning" : "Mika");
        await Build();
        var gameYpf = Path.Combine(_game, "ysbin.ypf");

        var inject = await _cli.RunAsync(["inject", "--yes", "--project", ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, inject.ExitCode);
        Assert.Equal(File.ReadAllBytes(Path.Combine(ProjectPath, "build", "ysbin.ypf")), File.ReadAllBytes(gameYpf));
        Assert.NotEqual(_scriptsYpf, File.ReadAllBytes(gameYpf));

        var restore = await _cli.RunAsync(["restore", "--yes", "--project", ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, restore.ExitCode);
        Assert.Equal(_scriptsYpf, File.ReadAllBytes(gameYpf));
    }
}
