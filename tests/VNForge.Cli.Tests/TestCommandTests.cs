using System.Text.Json;
using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
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

public sealed class TestCommandTests : IDisposable
{
    private const string YsbinYpf = "ysbin.ypf";
    private const string ProjectOption = "--project";
    private const string YesOption = "--yes";
    private const string Yst1 = "yst00001";
    private const string Yst2 = "yst00002";
    private const string ExeName = "MinhaVN.exe";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();
    private readonly Starter _starter = new();
    private readonly string _game;
    private readonly byte[] _originalYpf;

    public TestCommandTests()
    {
        _cli.Cli.GameProcessStarter = _starter;
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, ExeName), "MZ");
        _originalYpf = ScriptArchive();
        File.WriteAllBytes(Path.Combine(_game, YsbinYpf), _originalYpf);
    }

    public void Dispose() => _cli.Dispose();

    private string ProjectPath => Path.Combine(_cli.WorkingDirectory, "minha-vn-ptbr");

    private string GameYpf => Path.Combine(_game, YsbinYpf);

    private static SyntheticYstbBuilder NewScript() => new(Yscm);

    private static byte[] ScriptArchive()
    {
        var first = NewScript().WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        first.AddWord("美咲「おはよう」", 1).AddReturnCode(0, 2);
        first.AddCommand("GOSUB", 3, first.StringExpression("ES.CHAR.NAME"), first.StringExpression("美咲"));

        var second = NewScript().WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
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
        var init = await _cli.RunAsync(["init", "minha-vn-ptbr", "--game", _game], TestContext.Current.CancellationToken);
        Assert.Equal(0, init.ExitCode);
        var extract = await _cli.RunAsync(["extract", ProjectOption, ProjectPath], TestContext.Current.CancellationToken);
        Assert.Equal(0, extract.ExitCode);
    }

    private Task<RunResult> Test(params string[] args) =>
        _cli.RunAsync(["test", .. args, ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

    private void Translate(string key, Func<int, string> translation)
    {
        var project = ProjectFolder.Open(ProjectPath);
        var units = project.LoadText(key).Select((u, i) => u with { Translation = translation(i), Speaker = u.Speaker is null ? null : "Mika", Status = TranslationStatus.Translated }).ToList();
        project.SaveText(key, units);
    }

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
        var script = YstbReader.Read(bytes, name, commands);
        return YstbTextExtractor.Extract(new ScriptFile(name, bytes), script, commands).Units;
    }

    private string[] Backups() =>
        Directory.Exists(Path.Combine(ProjectPath, "backup")) ? Directory.GetDirectories(Path.Combine(ProjectPath, "backup")) : [];

    [Fact]
    public async Task Sem_yes_pergunta_e_recusar_sai_com_3_sem_gravar_nada()
    {
        await InitAndExtract();
        Translate(Yst1, i => $"Traduzido {i}");
        using var asking = new CliHarness(stdin: "n\n");
        asking.Cli.GameProcessStarter = _starter;

        var result = await asking.RunAsync(["test", ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("Gravar a tradução na pasta do jogo", result.Error);
        Assert.Equal(_originalYpf, await File.ReadAllBytesAsync(GameYpf, TestContext.Current.CancellationToken));
        Assert.Empty(Backups());
        Assert.Empty(_starter.Started);
    }

    [Fact]
    public async Task Testa_no_jogo_com_backup_e_abre_o_executavel_e_o_restore_devolve_byte_a_byte()
    {
        await InitAndExtract();
        Translate(Yst1, i => $"Traduzido {i}");

        var result = await Test(YesOption);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        Assert.Equal(string.Empty, result.Error);
        Assert.Contains("[1/5] Conferir a tradução", result.Output);
        Assert.Contains("[5/5] Abrir o jogo", result.Output);
        Assert.Contains("Jogo aberto:", result.Output);
        Assert.NotEqual(_originalYpf, await File.ReadAllBytesAsync(GameYpf, TestContext.Current.CancellationToken));
        var backup = Assert.Single(Backups());
        Assert.Equal(_originalYpf, await File.ReadAllBytesAsync(Path.Combine(backup, YsbinYpf), TestContext.Current.CancellationToken));
        Assert.Equal((Path.Combine(_game, ExeName), _game), Assert.Single(_starter.Started));
        Assert.NotNull(GameStateFile.Load(ProjectFolder.Open(ProjectPath)).InGame);

        var restore = await _cli.RunAsync(["restore", YesOption, ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, restore.ExitCode);
        Assert.Equal(_originalYpf, await File.ReadAllBytesAsync(GameYpf, TestContext.Current.CancellationToken));
        Assert.Null(GameStateFile.Load(ProjectFolder.Open(ProjectPath)).InGame);
    }

    [Fact]
    public async Task Accent_test_injeta_so_a_frase_fixa_na_primeira_fala()
    {
        await InitAndExtract();
        Translate(Yst2, i => $"Chuva {i}");
        var original = ReadEntries(GameYpf);

        var result = await Test("--accent-test", YesOption);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        var entries = ReadEntries(GameYpf);
        Assert.NotEqual(original["yst00001.ybn"], entries["yst00001.ybn"]);
        var spoken = Reextract("yst00001.ybn", entries["yst00001.ybn"]).Single(u => u.Kind == TextUnitKind.Dialogue);
        Assert.StartsWith("Teste de acentos: ", spoken.Source, StringComparison.Ordinal);
        Assert.Equal(GameTester.AccentSpeaker, spoken.Speaker);
        Assert.Equal(original["yst00002.ybn"], entries["yst00002.ybn"]);
        Assert.Equal(GameTestKind.AccentTest, GameStateFile.Load(ProjectFolder.Open(ProjectPath)).InGame?.Kind);

        var restore = await _cli.RunAsync(["restore", YesOption, ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, restore.ExitCode);
        Assert.Equal(_originalYpf, await File.ReadAllBytesAsync(GameYpf, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Erro_da_Conferencia_sai_com_2_e_nao_toca_no_jogo()
    {
        await InitAndExtract();
        Translate(Yst1, _ => string.Empty);
        _cli.Cli.Qa = QaEngine.Default;

        var result = await Test(YesOption);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(_originalYpf, await File.ReadAllBytesAsync(GameYpf, TestContext.Current.CancellationToken));
        Assert.Empty(Backups());
        Assert.Empty(_starter.Started);
    }

    [Fact]
    public async Task No_open_injeta_sem_abrir_o_jogo_e_json_traz_os_passos()
    {
        await InitAndExtract();
        Translate(Yst1, i => $"Traduzido {i}");

        var result = await Test(YesOption, "--no-open", "--json");

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using var document = JsonDocument.Parse(result.Output);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("translation", data.GetProperty("kind").GetString());
        Assert.Equal(["check", "build", "backup", "inject", "open"], data.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("step").GetString()));
        Assert.False(data.TryGetProperty("executable", out var executable) && executable.ValueKind != JsonValueKind.Null);
        Assert.Empty(_starter.Started);
    }

    [Theory]
    [InlineData("test")]
    [InlineData("inject")]
    [InlineData("restore")]
    public async Task Com_outra_operacao_no_jogo_o_comando_recusa_sem_gravar_nada(string verb)
    {
        await InitAndExtract();
        Translate(Yst1, i => $"Traduzido {i}");
        await Test(YesOption, "--no-open");
        var injected = await File.ReadAllBytesAsync(GameYpf, TestContext.Current.CancellationToken);
        var backups = Backups().Length;
        using var held = ProjectLock.Acquire(ProjectFolder.Open(ProjectPath));

        var result = await _cli.RunAsync([verb, YesOption, ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Outra operação do VNForge", result.Error);
        Assert.Equal(injected, await File.ReadAllBytesAsync(GameYpf, TestContext.Current.CancellationToken));
        Assert.Equal(backups, Backups().Length);
    }

    private sealed class Starter : IGameProcessStarter
    {
        public List<(string Executable, string WorkingDirectory)> Started { get; } = [];

        public void Start(string executablePath, string workingDirectory) => Started.Add((executablePath, workingDirectory));
    }
}
