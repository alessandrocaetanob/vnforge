using System.Text.Json;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;

namespace VNForge.Cli.Tests;

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
    public async Task State_mostra_original_traduzido_e_desconhecido_de_cada_jogo()
    {
        var game = await PrepareLinkedGame();
        var original = await Run("library", "state");

        Assert.Equal(0, original.ExitCode);
        Assert.Contains("Minha VN", original.Output, StringComparison.Ordinal);
        Assert.Contains("original", original.Output, StringComparison.Ordinal);

        var translated = await Run("library", "switch", game.Id, "--traduzido", "--yes");
        Assert.True(translated.ExitCode == 0, translated.Error + translated.Output);
        var after = await Run("library", "state", "Minha", "--json");
        var item = Data(after).GetProperty("games")[0];
        Assert.Equal("translated", item.GetProperty("state").GetString());
        Assert.Equal("translation", item.GetProperty("patch").GetProperty("kind").GetString());
        Assert.False(string.IsNullOrEmpty(item.GetProperty("patch").GetProperty("backup").GetString()));
        Assert.True(item.GetProperty("patch").TryGetProperty("appliedAt", out _));

        new LibraryFile(_cli.UserConfigDirectory).Save(new GameLibrary([game with { ProjectPath = null }]));
        var unknown = await Run("library", "state", game.Id);
        Assert.Contains("desconhecido", unknown.Output, StringComparison.Ordinal);
        Assert.Contains("projeto", unknown.Output, StringComparison.Ordinal);
    }

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

    [Fact]
    public async Task Switch_json_traz_a_acao_e_os_estados_antes_e_depois()
    {
        var game = await PrepareLinkedGame();

        var applied = Data(await Run("library", "switch", game.Id, "--traduzido", "--yes", "--json"));
        var again = Data(await Run("library", "switch", game.Id, "--traduzido", "--yes", "--json"));

        Assert.Equal("appliedTranslation", applied.GetProperty("action").GetString());
        Assert.Equal("original", applied.GetProperty("before").GetProperty("state").GetString());
        Assert.Equal("translated", applied.GetProperty("after").GetProperty("state").GetString());
        Assert.Equal("unchanged", again.GetProperty("action").GetString());
    }

    [Fact]
    public async Task Switch_sem_confirmar_nao_grava_nada_e_sai_com_3()
    {
        var game = await PrepareLinkedGame();
        var before = GameBytes;
        _cli.SetInput("n\n");
        var plain = await Run("library", "switch", game.Id, "--traduzido");

        Assert.Equal(3, plain.ExitCode);
        Assert.Equal(before, GameBytes);
        Assert.Empty(Directory.GetDirectories(Path.Combine(ProjectPath, "backup")));
    }

    [Theory]
    [InlineData]
    public async Task Switch_exige_exatamente_uma_das_duas_opcoes(params string[] options)
    {
        var game = await PrepareLinkedGame();

        var result = await Run(["library", "switch", game.Id, "--yes", .. options]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("--original", result.Error, StringComparison.Ordinal);
        Assert.Contains("--traduzido", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switch_de_jogo_sem_projeto_ligado_recusa_e_nao_toca_no_jogo()
    {
        var game = await PrepareLinkedGame(linked: false);
        var before = GameBytes;

        var result = await Run("library", "switch", game.Id, "--traduzido", "--yes");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("projeto", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, GameBytes);
    }

    [Fact]
    public async Task Backup_sem_estado_do_jogo_aparece_como_nao_registrado_e_traduzir_grava_a_traducao()
    {
        var game = await PrepareLinkedGame();
        Assert.Equal(0, (await Run("library", "switch", game.Id, "--traduzido", "--yes")).ExitCode);
        File.Delete(GameStateFile.PathFor(ProjectFolder.Open(ProjectPath)));

        var state = await Run("library", "state", game.Id);
        var json = Data(await Run("library", "state", game.Id, "--json")).GetProperty("games")[0];
        var again = Data(await Run("library", "switch", game.Id, "--traduzido", "--yes", "--json"));

        Assert.Contains("não registra o que ela gravou", state.Output, StringComparison.Ordinal);
        Assert.Equal("unknown", json.GetProperty("patch").GetProperty("kind").GetString());
        Assert.Equal("appliedTranslation", again.GetProperty("action").GetString());
    }

    private sealed class NoStarter : Core.Projects.IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory) => throw new InvalidOperationException("Trocar não abre o jogo.");
    }
}
