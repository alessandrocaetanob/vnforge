using System.Text.Json;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R1")]
public sealed class LibraryTranslationCommandTests : IDisposable
{
    private const string YsbinYpf = "ysbin.ypf";
    private const string ExeName = "MinhaVN.exe";
    private const string Yst1 = "yst00001";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();
    private readonly string _game;

    public LibraryTranslationCommandTests()
    {
        _cli.Cli.GameProcessStarter = new NoStarter();
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, ExeName), "MZ");
        File.WriteAllBytes(Path.Combine(_game, YsbinYpf), ScriptArchive());
    }

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string ProjectPath => Path.Combine(_cli.WorkingDirectory, "minha-vn-ptbr");

    private static byte[] ScriptArchive()
    {
        var first = new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        first.AddWord("美咲「おはよう」", 1).AddReturnCode(0, 2);
        first.AddCommand("GOSUB", 3, first.StringExpression("ES.CHAR.NAME"), first.StringExpression("美咲"));
        var list = new SyntheticYstlBuilder().WithVersion(500).AddScript(@"userscript\global.yst").AddScript(@"userscript\01_prologo.yst").Build();
        return new SyntheticYpfBuilder(500)
            .AddFile("ysc.ybn", Yscm.Bytes)
            .AddFile("yst_list.ybn", list.Bytes)
            .AddFile("yst00001.ybn", first.Build().Bytes)
            .Build().Bytes;
    }

    private async Task<LibraryGame> PrepareLinkedGame(bool linked = true)
    {
        Assert.Equal(0, (await _cli.RunAsync(["init", "minha-vn-ptbr", "--game", _game], Ct)).ExitCode);
        Assert.Equal(0, (await _cli.RunAsync(["extract", "--project", ProjectPath], Ct)).ExitCode);
        var project = ProjectFolder.Open(ProjectPath);
        project.SaveText(Yst1, [.. project.LoadText(Yst1).Select((u, i) => u with { Translation = $"Traduzido {i}", Speaker = u.Speaker is null ? null : "Mika", Status = Core.Text.TranslationStatus.Translated })]);
        var game = new LibraryGame(GameLibrary.IdFor(_game), GameLibrary.NormalizeFolder(_game), ExeName, LibraryEngine.Unknown, "Minha VN", null, DateTimeOffset.UnixEpoch)
        {
            ProjectPath = linked ? GameLibrary.NormalizeFolder(ProjectPath) : null,
        };
        new LibraryFile(_cli.UserConfigDirectory).Save(new GameLibrary([game]));
        return game;
    }

    private async Task<RunResult> Run(params string[] args) => await _cli.RunAsync(args, Ct);

    private static JsonElement Data(RunResult result) => JsonDocument.Parse(result.Output).RootElement.GetProperty("data");

    private byte[] GameBytes => File.ReadAllBytes(Path.Combine(_game, YsbinYpf));

    [Fact]
    public async Task Switch_traduzido_e_original_deixa_o_arquivo_do_jogo_identico_byte_a_byte()
    {
        var game = await PrepareLinkedGame();
        var before = GameBytes;

        var toTranslated = await Run("library", "switch", game.Id, "--traduzido", "--yes");

        Assert.True(toTranslated.ExitCode == 0, toTranslated.Error + toTranslated.Output);
        Assert.NotEqual(before, GameBytes);
        Assert.Single(Directory.GetDirectories(Path.Combine(ProjectPath, "backup")));
        Assert.Contains("traduzido", toTranslated.Output, StringComparison.Ordinal);

        var toOriginal = await Run("library", "switch", game.Id, "--original", "--yes");

        Assert.True(toOriginal.ExitCode == 0, toOriginal.Error + toOriginal.Output);
        Assert.Equal(before, GameBytes);
        Assert.Contains("original", toOriginal.Output, StringComparison.Ordinal);
        Assert.Null(GameStateFile.Load(ProjectFolder.Open(ProjectPath)).InGame);
    }

    private sealed class NoStarter : Core.Projects.IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory) => throw new InvalidOperationException("Trocar não abre o jogo.");
    }
}
