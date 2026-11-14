using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;

namespace VNForge.Cli.Tests;

/// <summary>
/// Os cinco cenários de ponta a ponta do plano de testes (T3, #700): cada um roda os comandos do CLI de verdade sobre um jogo YU-RIS sintético
/// (um <c>ysbin.ypf</c> e um <c>bgm.ypf</c> montados em código), com o provedor falso no lugar da IA.
/// </summary>
public sealed class CenarioDePontaAPontaTests : IDisposable
{
    private const string YsbinYpf = "ysbin.ypf";
    private const string BgmYpf = "bgm.ypf";
    private const string ProjectOption = "--project";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();
    private readonly string _game;

    public CenarioDePontaAPontaTests()
    {
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "MinhaVN.exe"), "MZ");
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

    private async Task InitExtractTranslateBuild(string encoding = "sjis")
    {
        var init = await _cli.RunAsync(["init", "minha-vn-ptbr", "--game", _game], TestContext.Current.CancellationToken);
        Assert.Equal(0, init.ExitCode);
        var project = ProjectFolder.Open(ProjectPath);
        project.SaveConfig(project.LoadConfig() with { Encoding = new EncodingConfig(encoding) });
        await Ok("extract");
        await Ok("translate");
        await Ok("build");
    }

    private void Retranslate(string key, string translation)
    {
        var project = ProjectFolder.Open(ProjectPath);
        project.SaveText(key, [.. project.LoadText(key).Select(u => u with { Translation = translation, Status = TranslationStatus.Translated })]);
    }

    private string GameFile(string name) => Path.Combine(_game, name);

    [Fact]
    [Trait("Risco", "R1")]
    public async Task Cenario_extrair_traduzir_injetar_e_restaurar_devolve_o_jogo_byte_a_byte()
    {
        var originalScripts = await File.ReadAllBytesAsync(GameFile(YsbinYpf), TestContext.Current.CancellationToken);
        var originalBgm = await File.ReadAllBytesAsync(GameFile(BgmYpf), TestContext.Current.CancellationToken);
        await InitExtractTranslateBuild();

        await Ok("inject", "--yes");

        Assert.NotEqual(originalScripts, await File.ReadAllBytesAsync(GameFile(YsbinYpf), TestContext.Current.CancellationToken));
        await Ok("restore", "--yes");
        Assert.Equal(originalScripts, await File.ReadAllBytesAsync(GameFile(YsbinYpf), TestContext.Current.CancellationToken));
        Assert.Equal(originalBgm, await File.ReadAllBytesAsync(GameFile(BgmYpf), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Risco", "R1")]
    public async Task Cenario_injetar_por_cima_de_uma_injecao_e_restaurar_volta_ao_original_e_nao_a_primeira_injecao()
    {
        var original = await File.ReadAllBytesAsync(GameFile(YsbinYpf), TestContext.Current.CancellationToken);
        await InitExtractTranslateBuild();
        await Ok("inject", "--yes");
        var firstInjection = await File.ReadAllBytesAsync(GameFile(YsbinYpf), TestContext.Current.CancellationToken);
        Retranslate("yst00001", "Segunda versao");
        await Ok("build");

        await Ok("inject", "--yes");

        var secondInjection = await File.ReadAllBytesAsync(GameFile(YsbinYpf), TestContext.Current.CancellationToken);
        Assert.NotEqual(firstInjection, secondInjection);
        await Ok("restore", "--yes");
        Assert.Equal(original, await File.ReadAllBytesAsync(GameFile(YsbinYpf), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Risco", "R1")]
    public async Task Cenario_jogo_que_mudou_de_pasta_liga_ao_projeto_e_a_restauracao_acha_o_backup()
    {
        var original = await File.ReadAllBytesAsync(GameFile(YsbinYpf), TestContext.Current.CancellationToken);
        await InitExtractTranslateBuild();
        await Ok("inject", "--yes");
        var injected = await File.ReadAllBytesAsync(GameFile(YsbinYpf), TestContext.Current.CancellationToken);
        var moved = Path.Combine(_cli.Root, "jogo-novo", "MinhaVN");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(_game, moved);

        await Ok("game", "move", "--game", moved);

        await Ok("restore", "--yes");
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(moved, YsbinYpf), TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(_game));
        await Ok("inject", "--yes");
        Assert.Equal(injected, await File.ReadAllBytesAsync(Path.Combine(moved, YsbinYpf), TestContext.Current.CancellationToken));
    }
}

