using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;
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

    [Fact]
    public async Task Gera_instalador_patch_e_textos_sem_tocar_na_pasta_do_jogo()
    {
        await InitAndExtract();
        Translate(Yst1, "Traduzido");

        var result = await Release("1.0.0", "--credits", "Tradução: Equipe Chuva Mansa");

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        Assert.Equal(_originalYpf, await File.ReadAllBytesAsync(Path.Combine(_game, YsbinYpf), Token));
        Assert.False(Directory.Exists(Path.Combine(_game, PatchInstaller.BackupFolderName)));
        var names = Directory.GetFiles(Out("1.0.0")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Contains("CREDITOS.txt", names);
        Assert.Contains("LEIA-ME.txt", names);
        Assert.Contains("MUDANCAS.txt", names);
        Assert.Contains("LICENCAS.txt", names);
        Assert.Contains(names, n => n!.EndsWith(".vnfpatch", StringComparison.Ordinal));
        Assert.Contains(names, n => n!.EndsWith("-PT-BR-1.0.0.exe", StringComparison.Ordinal));
        Assert.Contains("Tradução: Equipe Chuva Mansa", await File.ReadAllTextAsync(Path.Combine(Out("1.0.0"), "CREDITOS.txt"), Token));
        Assert.Contains("idênticos ao build", result.Output);
    }

    [Fact]
    public async Task O_instalador_gerado_aplica_sobre_uma_copia_do_jogo_e_a_desinstalacao_devolve_os_originais()
    {
        await InitAndExtract();
        Translate(Yst1, "Traduzido");
        Assert.Equal(0, (await Release("1.0.0")).ExitCode);
        var copy = Path.Combine(_cli.Root, "copia-do-jogo");
        Directory.CreateDirectory(copy);
        foreach (var file in Directory.GetFiles(_game))
        {
            File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
        }

        var installer = Directory.GetFiles(Out("1.0.0"), "*.exe").Single();
        using (var bundle = InstallerBundle.TryOpen(installer))
        {
            Assert.NotNull(bundle);
            Assert.DoesNotContain(bundle.Manifest.Files, f => f.Path.EndsWith("MinhaVN.exe", StringComparison.Ordinal));
            PatchInstaller.Install(copy, bundle, null, Token);
        }

        var build = await File.ReadAllBytesAsync(Path.Combine(ProjectPath, "build", YsbinYpf), Token);
        Assert.Equal(build, await File.ReadAllBytesAsync(Path.Combine(copy, YsbinYpf), Token));

        PatchInstaller.Uninstall(copy, null, Token);

        Assert.Equal(_originalYpf, await File.ReadAllBytesAsync(Path.Combine(copy, YsbinYpf), Token));
    }

    private PatchManifest ManifestOf(string version)
    {
        using var bundle = InstallerBundle.TryOpen(Directory.GetFiles(Out(version), "*.exe").Single());
        return bundle!.Manifest;
    }

    [Fact]
    public async Task Capa_e_versao_do_jogo_entram_no_manifesto_do_patch()
    {
        await InitAndExtract();
        Translate(Yst1, "Traduzido");
        var cover = Path.Combine(_cli.Root, "capa.png");
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        await File.WriteAllBytesAsync(cover, png, Token);

        var result = await Release("1.0.0", "--capa", cover, "--versao-do-jogo", " 1.00 ");

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        var manifest = ManifestOf("1.0.0");
        Assert.Equal(png, manifest.Cover);
        Assert.Equal("1.00", manifest.GameVersion);
    }

    [Fact]
    public async Task Sem_capa_e_sem_versao_o_patch_nao_leva_nenhuma_das_duas()
    {
        await InitAndExtract();
        Translate(Yst1, "Traduzido");

        var result = await Release("1.0.0");

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        var manifest = ManifestOf("1.0.0");
        Assert.Null(manifest.Cover);
        Assert.Null(manifest.GameVersion);
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
    public async Task Jogo_que_mudou_desde_a_extracao_nao_gera_nada()
    {
        await InitAndExtract();
        Translate(Yst1, "Traduzido");
        await File.WriteAllBytesAsync(Path.Combine(_game, YsbinYpf), [.. _originalYpf, 0], Token);

        var result = await Release("1.0.0");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(Out("1.0.0")) && Directory.GetFiles(Out("1.0.0")).Length > 0);
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
