using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Patching;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Cli.Tests;

/// <summary>
/// Os cinco cenários de ponta a ponta do plano de testes (T3, #700): cada um roda os comandos do CLI de verdade sobre um jogo YU-RIS sintético
/// (um <c>ysbin.ypf</c> e um <c>bgm.ypf</c> montados em código), com o provedor falso no lugar da IA.
/// </summary>
[Trait("Risco", "R1")]
[Trait("Risco", "R2")]
[Trait("Risco", "R3")]
[Trait("Risco", "R4")]
public sealed class CenarioDePontaAPontaTests : IDisposable
{
    private const string YsbinYpf = "ysbin.ypf";
    private const string BgmYpf = "bgm.ypf";
    private const string GameExe = "MinhaVN.exe";
    private const string ProjectOption = "--project";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();
    private readonly string _game;

    public CenarioDePontaAPontaTests()
    {
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, GameExe), "MZ");
        File.WriteAllBytes(Path.Combine(_game, YsbinYpf), ScriptArchive());
        File.WriteAllBytes(Path.Combine(_game, BgmYpf), new SyntheticYpfBuilder(500).AddFile("bgm01.ogg", [1, 2, 3]).Build().Bytes);
        _cli.Cli.TranslationProviderFactory = (_, _) => new FakeTranslationProvider();
    }

    public void Dispose() => _cli.Dispose();

    private string ProjectPath => Path.Combine(_cli.WorkingDirectory, "minha-vn-ptbr");

    private static byte[] ScriptArchive()
    {
        var first = new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        first.AddWord("美咲「おはよう」", 1).AddReturnCode(0, 2);
        first.AddCommand("GOSUB", 3, first.StringExpression("ES.CHAR.NAME"), first.StringExpression("美咲"));

        var second = new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
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

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync([.. args, ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

    private async Task Ok(params string[] args)
    {
        var result = await Run(args);
        Assert.True(result.ExitCode == 0, $"vnf {string.Join(' ', args)} saiu com {result.ExitCode}: {result.Output}{result.Error}");
    }

    private async Task Init(string encoding = "sjis")
    {
        var init = await _cli.RunAsync(["init", "minha-vn-ptbr", "--game", _game], TestContext.Current.CancellationToken);
        Assert.Equal(0, init.ExitCode);
        var project = ProjectFolder.Open(ProjectPath);
        project.SaveConfig(project.LoadConfig() with { Encoding = new EncodingConfig(encoding) });
    }

    private async Task InitExtractTranslateBuild()
    {
        await Init();
        await Ok("extract");
        await Ok("translate");
        await Ok("build");
    }

    private void Retranslate(string key, string translation)
    {
        var project = ProjectFolder.Open(ProjectPath);
        project.SaveText(key, [.. project.LoadText(key).Select(u => u with { Translation = translation, Status = TranslationStatus.Translated })]);
    }

    private string BuiltYpf => Path.Combine(ProjectPath, "build", YsbinYpf);

    private string GameFile(string name) => Path.Combine(_game, name);

    private static Task<byte[]> ReadFile(string path) => File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

    private Task<byte[]> ReadGame(string name, string? folder = null) => ReadFile(Path.Combine(folder ?? _game, name));

    private static byte[] ShiftJis(string text) => GameEncodings.ShiftJis.GetBytes(text);

    [Fact]
    [Trait("Risco", "R1")]
    public async Task Cenario_extrair_traduzir_injetar_e_restaurar_devolve_o_jogo_byte_a_byte()
    {
        var originalScripts = await ReadGame(YsbinYpf);
        var originalBgm = await ReadGame(BgmYpf);
        await InitExtractTranslateBuild();

        await Ok("inject", "--yes");

        Assert.Equal(ShiftJis("美咲「[PT] おはよう」"), await FirstMessage(GameFile(YsbinYpf), "yst00001"));
        await Ok("restore", "--yes");
        Assert.Equal(originalScripts, await ReadGame(YsbinYpf));
        Assert.Equal(originalBgm, await ReadGame(BgmYpf));
    }

    [Fact]
    [Trait("Risco", "R1")]
    public async Task Cenario_injetar_por_cima_de_uma_injecao_e_restaurar_volta_ao_original_e_nao_a_primeira_injecao()
    {
        var original = await ReadGame(YsbinYpf);
        var originalBgm = await ReadGame(BgmYpf);
        var originalExe = await ReadGame(GameExe);
        await InitExtractTranslateBuild();
        await Ok("inject", "--yes");
        Retranslate("yst00001", "Segunda versao");
        await Ok("build");

        await Ok("inject", "--yes");

        Assert.Equal(ShiftJis("美咲「Segunda versao」"), await FirstMessage(GameFile(YsbinYpf), "yst00001"));
        await Ok("restore", "--yes");
        Assert.Equal(original, await ReadGame(YsbinYpf));
        Assert.Equal(originalBgm, await ReadGame(BgmYpf));
        Assert.Equal(originalExe, await ReadGame(GameExe));
    }

    [Fact]
    [Trait("Risco", "R1")]
    [Trait("Risco", "R6")]
    public async Task Cenario_jogo_que_mudou_de_pasta_liga_ao_projeto_e_a_restauracao_acha_o_backup()
    {
        var original = await ReadGame(YsbinYpf);
        await InitExtractTranslateBuild();
        await Ok("inject", "--yes");
        var injected = await ReadGame(YsbinYpf);
        var moved = Path.Combine(_cli.Root, "jogo-novo", "MinhaVN");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(_game, moved);
        // Quem copia em vez de mover deixa o jogo, ainda com a tradução, na pasta antiga.
        Directory.CreateDirectory(_game);
        foreach (var file in Directory.GetFiles(moved))
        {
            File.Copy(file, Path.Combine(_game, Path.GetFileName(file)));
        }

        await Ok("game", "move", "--game", moved);

        await Ok("restore", "--yes");
        Assert.Equal(original, await ReadGame(YsbinYpf, moved));
        Assert.Equal(injected, await ReadGame(YsbinYpf));
        await Ok("inject", "--yes");
        Assert.Equal(injected, await ReadGame(YsbinYpf, moved));
    }

    private static async Task<byte[]> FirstMessage(string ypfPath, string script)
    {
        await using var stream = File.OpenRead(ypfPath);
        using var archive = (YpfArchive)new YpfArchiveFormat().Open(stream);
        var entry = archive.Entries.Single(e => e.Path == script + ".ybn");
        using var content = archive.OpenEntry(entry);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, TestContext.Current.CancellationToken);
        var ystb = YstbReader.Read(buffer.ToArray(), entry.Path, YscmReader.Read(Yscm.Bytes, "ysc.ybn"));
        return ystb.GetResource(ystb.Instructions[0].Arguments[0]).ToArray();
    }

    [Fact]
    [Trait("Risco", "R4")]
    public async Task Cenario_texto_dificil_em_cp1252_sai_no_ybn_com_os_bytes_esperados()
    {
        await Init("cp1252");
        var project = ProjectFolder.Open(ProjectPath);
        project.SaveConfig(project.LoadConfig() with { Style = project.LoadConfig().Style with { SpeakerDelimiter = SpeakerDelimiterStyle.Guillemets } });
        await File.WriteAllTextAsync(project.FormattingPath, """{ "wrap": { "enabled": true, "maxWidth": 6, "charWidth": "uniform" } }""", TestContext.Current.CancellationToken);
        await Ok("extract");
        project.SaveText("yst00001", [.. project.LoadText("yst00001").Select((u, i) => i == 0
            ? u with { Speaker = "Inui", Translation = "Já\\nera tarde, até já", Status = TranslationStatus.Translated }
            : u)]);
        project.SaveText("yst00002", [.. project.LoadText("yst00002").Select((u, i) => i == 0
            ? u with { Translation = "Ele já não sabia de nada", Status = TranslationStatus.Translated }
            : u)]);

        await Ok("build");

        // cp1252: á E1, é E9, ã E3, « AB, » BB. EF F0 é a quebra de linha do jogo; depois de acento ela leva um 0x20 antes (#435), e a quebra automática da caixa de 6 colunas cai nos mesmos pontos.
        byte[] dialogue = [.. "Inui"u8, 0xAB, .. "J"u8, 0xE1, 0x20, 0xEF, 0xF0, .. "era"u8, 0xEF, 0xF0, .. "tarde,"u8, 0xEF, 0xF0, .. "at"u8, 0xE9, 0x20, .. "j"u8, 0xE1, 0xBB];
        byte[] narration = [.. "Ele j"u8, 0xE1, 0x20, 0xEF, 0xF0, .. "n"u8, 0xE3, .. "o"u8, 0xEF, 0xF0, .. "sabia"u8, 0xEF, 0xF0, .. "de"u8, 0xEF, 0xF0, .. "nada"u8];
        Assert.Equal(dialogue, await FirstMessage(BuiltYpf, "yst00001"));
        Assert.Equal(narration, await FirstMessage(BuiltYpf, "yst00002"));
    }

    [Fact]
    [Trait("Risco", "R2")]
    public async Task Cenario_projeto_gravado_num_disco_que_zera_o_rename_sai_com_erro_em_portugues_e_sem_arquivo_vazio()
    {
        await Init();
        await Ok("extract");
        var before = Snapshot(ProjectPath);
        Assert.NotEmpty(before);

        RunResult result;
        using (ProjectDisk.Use(new ZeroingDisk(_ => true)))
        {
            result = await Run("translate");
        }

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(Path.Combine(ProjectPath, "text"), result.Error);
        var after = Snapshot(ProjectPath);
        Assert.DoesNotContain(after, file => file.Value.Length == 0);
        // A copia .bak que a troca deixa ao lado e o log do comando ficam de fora; o conjunto de arquivos e o conteudo de cada um tem de ser o de antes (nenhum arquivo novo, nem vazio, nem o cache).
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (name, content) in before)
        {
            Assert.Equal(content, after[name]);
        }
    }

    private static Dictionary<string, byte[]> Snapshot(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(f => (Relative: Path.GetRelativePath(directory, f), Path: f))
            .Where(f => !f.Relative.EndsWith(".bak", StringComparison.Ordinal) && !f.Relative.StartsWith(Path.Combine(".cache", "logs"), StringComparison.Ordinal))
            .ToDictionary(f => f.Relative, f => File.ReadAllBytes(f.Path));
}
