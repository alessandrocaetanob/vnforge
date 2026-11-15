using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Publishing;
using VNForge.Core.Text;
using VNForge.Engines.YuRis;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;
using VNForge.Patching;

namespace VNForge.Cli.Tests;

public sealed class ReleaseCommandTests : IDisposable
{
    private const string ProjectOption = "--project";
    private const string YsbinYpf = "ysbin.ypf";
    private const string Yst1 = "yst00001";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();
    private readonly string _game;
    private readonly string _stub;
    private readonly byte[] _originalYpf;

    public ReleaseCommandTests()
    {
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "MinhaVN.exe"), "MZ");
        _originalYpf = ScriptArchive();
        File.WriteAllBytes(Path.Combine(_game, YsbinYpf), _originalYpf);
        _stub = Path.Combine(_cli.Root, "vnf-instalador.exe");
        File.WriteAllBytes(_stub, [.. "MZ instalador de teste"u8]);
    }

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string ProjectPath => Path.Combine(_cli.WorkingDirectory, "minha-vn-ptbr");

    private static byte[] ScriptArchive()
    {
        var first = new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        first.AddWord("美咲「おはよう」", 1).AddReturnCode(0, 2);
        first.AddCommand("GOSUB", 3, first.StringExpression("ES.CHAR.NAME"), first.StringExpression("美咲"));
        var second = new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        second.AddWord("雨が降っていた。", 1).AddReturnCode(0, 2);

        var list = new SyntheticYstlBuilder().WithVersion(500).AddScript(@"userscript\global.yst").AddScript(@"userscript\01_prologo.yst").Build();
        return new SyntheticYpfBuilder(500)
            .AddFile("ysc.ybn", Yscm.Bytes)
            .AddFile("yst_list.ybn", list.Bytes)
            .AddFile("yst00001.ybn", first.Build().Bytes)
            .AddFile("yst00002.ybn", second.Build().Bytes)
            .Build().Bytes;
    }

    private async Task InitAndExtract()
    {
        Assert.Equal(0, (await _cli.RunAsync(["init", "minha-vn-ptbr", "--game", _game], Token)).ExitCode);
        Assert.Equal(0, (await _cli.RunAsync(["extract", ProjectOption, ProjectPath], Token)).ExitCode);
    }

    private void Translate(string key, string prefix)
    {
        var project = ProjectFolder.Open(ProjectPath);
        var units = project.LoadText(key)
            .Select((u, i) => u with { Translation = $"{prefix} {i}", Speaker = u.Speaker is null ? null : "Mika", Status = TranslationStatus.Translated })
            .ToList();
        project.SaveText(key, units);
    }

    private Task<RunResult> Release(string version, params string[] extra) =>
        _cli.RunAsync(["release", version, "--installer", _stub, .. extra, ProjectOption, ProjectPath], Token);

    private string Out(string version) => Path.Combine(ProjectPath, "releases", version);

    private PatchManifest ManifestOf(string version)
    {
        using var bundle = InstallerBundle.TryOpen(Directory.GetFiles(Out(version), "*.exe").Single());
        return bundle!.Manifest;
    }

    [Fact]
    public async Task Versao_do_jogo_invalida_e_recusada_e_nada_e_gravado()
    {
        await InitAndExtract();
        Translate(Yst1, "Traduzido");

        var result = await Release("1.0.0", "--versao-do-jogo", new string('9', GameConfig.MaxVersionLength + 1));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Versão do jogo inválida", result.Error);
        Assert.Null(ProjectFolder.Open(ProjectPath).LoadConfig().Game.Version);
        Assert.False(Directory.Exists(Path.Combine(ProjectPath, "releases")));
    }

    [Theory]
    [InlineData("texto", "não é PNG, JPEG nem WebP")]
    [InlineData("grande", "aceita até")]
    [InlineData("falta", "Não achei a capa")]
    public async Task Capa_invalida_e_recusada_antes_do_build(string kind, string message)
    {
        await InitAndExtract();
        var cover = Path.Combine(_cli.Root, "capa.bin");
        if (kind == "texto")
        {
            await File.WriteAllTextAsync(cover, "não sou imagem", Token);
        }
        else if (kind == "grande")
        {
            await File.WriteAllBytesAsync(cover, [0x89, 0x50, 0x4E, 0x47, .. new byte[PatchPackage.MaxCoverBytes]], Token);
        }

        var result = await Release("1.0.0", "--capa", cover);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(message, result.Error);
        Assert.False(Directory.Exists(Path.Combine(ProjectPath, "releases")));
    }

    [Fact]
    public async Task A_segunda_versao_lista_as_cenas_que_mudaram_desde_a_primeira()
    {
        await InitAndExtract();
        Translate(Yst1, "Traduzido");
        Assert.Equal(0, (await Release("1.0.0")).ExitCode);
        Translate(Yst1, "Revisado");

        var result = await Release("1.1.0", "--json");

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using var document = JsonDocument.Parse(result.Output);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("1.0.0", data.GetProperty("previousVersion").GetString());
        Assert.True(data.GetProperty("changedLines").GetInt32() > 0);
        var changes = await File.ReadAllTextAsync(Path.Combine(Out("1.1.0"), "MUDANCAS.txt"), Token);
        Assert.Contains("Mudanças desde a 1.0.0", changes);
        Assert.Contains(Yst1, changes);
    }

    [Fact]
    public async Task Versao_invalida_e_instalador_ausente_sao_recusados_antes_do_build()
    {
        await InitAndExtract();

        var invalid = await Release("../x");
        var missing = await _cli.RunAsync(["release", "1.0.0", "--installer", Path.Combine(_cli.Root, "nao-existe.exe"), ProjectOption, ProjectPath], Token);

        Assert.NotEqual(0, invalid.ExitCode);
        Assert.Contains("Versão inválida", invalid.Error);
        Assert.NotEqual(0, missing.ExitCode);
        Assert.Contains("executável do instalador", missing.Error);
        Assert.False(Directory.Exists(Path.Combine(ProjectPath, "releases")));
    }
}
