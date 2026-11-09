using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Injection;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Library;

public sealed class LibraryTranslationTests : IDisposable
{
    private const string Package = FakeEngine.PackageName;
    private const string GameExe = "Jogo.exe";

    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly ProjectFolder _project;
    private readonly FakeEngine _engine = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero)) { AutoAdvanceAmount = TimeSpan.FromMilliseconds(10) };

    public LibraryTranslationTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "a001.fake"), "dialogue|Olá\nname|Ana");
        File.WriteAllText(Path.Combine(_game, "a002.fake"), "narration|Chovia.");
        File.WriteAllText(Path.Combine(_game, GameExe), "MZ");
        _project = NewProject(Path.Combine(_temp.Path, "projeto"), _game);
        _project.SaveState(new PipelineState([], new Dictionary<string, StageState>(), _game));
        _engine.Unpack(_game, _project.GameCacheDirectory, TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(_game, Package), "dialogue|Olá\nname|Ana\u0001narration|Chovia.");
        foreach (var key in new[] { "a001", "a002" })
        {
            var script = _engine.Scripts[0].Extract(new Engines.ScriptFile(key + ".fake", File.ReadAllBytes(Path.Combine(_project.GameCacheDirectory, Package, key + ".fake"))), new FakeContext());
            _project.SaveText(key, script.Units);
        }

        var units = _project.LoadText("a001").Select((u, i) => i == 0 ? u with { Translation = "Hello", Status = TranslationStatus.Translated } : u).ToList();
        _project.SaveText("a001", units);
    }

    public void Dispose() => _temp.Dispose();

    private static ProjectFolder NewProject(string folder, string gameFolder) =>
        ProjectFolder.Create(
            folder,
            new ProjectConfig(
                "Teste",
                new GameConfig(gameFolder, GameExe),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));

    private LibraryGame Game(string? projectPath = "") =>
        new(
            GameLibrary.IdFor(_game),
            GameLibrary.NormalizeFolder(_game),
            GameExe,
            new LibraryEngine("fake", "Fake", LibraryEngineSource.Engine),
            "Jogo",
            null,
            DateTimeOffset.UnixEpoch)
        {
            ProjectPath = projectPath == string.Empty ? _project.Root : projectPath,
        };

    private LibraryTranslationSwitcher Switcher() => new(new GameTester([_engine], new NoStarter(), new QaEngine([]), _clock));

    private static Dictionary<string, byte[]> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(folder, f), File.ReadAllBytes);

    private string[] BackupFolders() =>
        Directory.Exists(_project.BackupDirectory) ? Directory.GetDirectories(_project.BackupDirectory) : [];

    [Fact]
    public void Jogo_sem_injecao_esta_original()
    {
        var state = LibraryTranslationReader.Read(Game());

        Assert.Equal(LibraryTranslationKind.Original, state.Kind);
        Assert.Null(state.Patch);
    }

    [Fact]
    public void Jogo_sem_projeto_ligado_e_desconhecido_e_nao_pode_trocar()
    {
        var game = Game(projectPath: null);
        var before = Snapshot(_game);

        var state = LibraryTranslationReader.Read(game);

        Assert.Equal(LibraryTranslationKind.Unknown, state.Kind);
        Assert.Contains("projeto", state.Reason, StringComparison.Ordinal);
        Assert.Throws<LibraryException>(() => Switcher().ToTranslated(game, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<LibraryException>(() => Switcher().ToOriginal(game));
        Assert.Equal(before.Keys.Order(), Snapshot(_game).Keys.Order());
    }

    [Fact]
    public void Projeto_que_nao_abre_e_desconhecido()
    {
        var state = LibraryTranslationReader.Read(Game(Path.Combine(_temp.Path, "nao-existe")));

        Assert.Equal(LibraryTranslationKind.Unknown, state.Kind);
        Assert.False(string.IsNullOrWhiteSpace(state.Reason));
    }

    [Fact]
    public void Projeto_de_outra_pasta_de_jogo_e_desconhecido_e_nunca_grava_nela()
    {
        var other = Path.Combine(_temp.Path, "outro-jogo");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, GameExe), "MZ");
        var foreign = NewProject(Path.Combine(_temp.Path, "projeto-de-outro"), other);
        var game = Game(foreign.Root);
        var before = Snapshot(_game);

        var state = LibraryTranslationReader.Read(game);

        Assert.Equal(LibraryTranslationKind.Unknown, state.Kind);
        Assert.Contains("outra pasta", state.Reason, StringComparison.Ordinal);
        Assert.Throws<LibraryException>(() => Switcher().ToTranslated(game, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(before.Keys.Order(), Snapshot(_game).Keys.Order());
        Assert.Empty(BackupFolders());
    }

    [Fact]
    public void Traduzir_faz_backup_injeta_e_o_estado_passa_a_traduzido_com_a_versao()
    {
        var result = Switcher().ToTranslated(Game(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(LibraryTranslationAction.AppliedTranslation, result.Action);
        Assert.Equal(LibraryTranslationKind.Original, result.Before.Kind);
        Assert.Equal(LibraryTranslationKind.Translated, result.After.Kind);
        Assert.Equal("dialogue|Hello\nname|Ana\u0001narration|Chovia.", File.ReadAllText(Path.Combine(_game, Package)));
        var backup = Assert.Single(BackupFolders());
        Assert.Equal("dialogue|Olá\nname|Ana\u0001narration|Chovia.", File.ReadAllText(Path.Combine(backup, Package)));
        var patch = result.After.Patch!;
        Assert.Equal(GameTestKind.Translation, patch.Kind);
        Assert.Equal(Path.GetFileName(backup), patch.BackupDirectory);
        Assert.False(string.IsNullOrEmpty(patch.TextFingerprint));
        Assert.Equal(patch, LibraryTranslationReader.Read(Game()).Patch);
    }

    [Fact]
    public void Nao_abre_o_executavel_do_jogo_ao_trocar()
    {
        var starter = new CountingStarter();
        var switcher = new LibraryTranslationSwitcher(new GameTester([_engine], starter, new QaEngine([]), _clock));

        switcher.ToTranslated(Game(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, starter.Calls);
    }

    [Fact]
    public void Ida_e_volta_original_traduzido_original_deixa_os_arquivos_do_jogo_identicos_byte_a_byte()
    {
        var before = Snapshot(_game);
        var switcher = Switcher();

        var translated = switcher.ToTranslated(Game(), cancellationToken: TestContext.Current.CancellationToken);
        var inGame = Snapshot(_game);
        var back = switcher.ToOriginal(Game());

        Assert.NotEqual(before[Package], inGame[Package]);
        Assert.Equal(LibraryTranslationAction.ReturnedToOriginal, back.Action);
        Assert.Equal(LibraryTranslationKind.Translated, back.Before.Kind);
        Assert.Equal(LibraryTranslationKind.Original, back.After.Kind);
        Assert.Equal(translated.After.Patch, back.Before.Patch);
        var after = Snapshot(_game);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (path, bytes) in before)
        {
            Assert.True(bytes.SequenceEqual(after[path]), $"'{path}' mudou depois da ida e volta.");
        }
    }

    [Fact]
    public void Duas_idas_e_voltas_seguidas_continuam_identicas_ao_original()
    {
        var before = Snapshot(_game);
        var switcher = Switcher();

        for (var round = 0; round < 2; round++)
        {
            switcher.ToTranslated(Game(), cancellationToken: TestContext.Current.CancellationToken);
            switcher.ToOriginal(Game());
        }

        var after = Snapshot(_game);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        Assert.All(before, pair => Assert.True(pair.Value.SequenceEqual(after[pair.Key]), $"'{pair.Key}' mudou."));
    }

    [Fact]
    public void Pedir_o_estado_em_que_o_jogo_ja_esta_nao_grava_nada()
    {
        var switcher = Switcher();
        var original = switcher.ToOriginal(Game());
        Assert.Equal(LibraryTranslationAction.Unchanged, original.Action);
        Assert.Empty(BackupFolders());

        switcher.ToTranslated(Game(), cancellationToken: TestContext.Current.CancellationToken);
        var inGame = Snapshot(_game);
        var backups = BackupFolders();

        var again = switcher.ToTranslated(Game(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(LibraryTranslationAction.Unchanged, again.Action);
        Assert.Equal(backups, BackupFolders());
        Assert.All(inGame, pair => Assert.True(pair.Value.SequenceEqual(Snapshot(_game)[pair.Key])));
    }

    [Fact]
    public void Teste_de_acentos_no_jogo_conta_como_traduzido_e_traduzir_poe_a_traducao_inteira_sem_perder_o_original()
    {
        var before = Snapshot(_game);
        new GameTester([_engine], new NoStarter(), new QaEngine([]), _clock).Run(_project, new GameTestOptions(AccentTest: true, OpenGame: false), cancellationToken: TestContext.Current.CancellationToken);
        var state = LibraryTranslationReader.Read(Game());
        Assert.Equal(LibraryTranslationKind.Translated, state.Kind);
        Assert.Equal(GameTestKind.AccentTest, state.Patch!.Kind);

        var switcher = Switcher();
        var applied = switcher.ToTranslated(Game(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(LibraryTranslationAction.AppliedTranslation, applied.Action);
        Assert.Equal(GameTestKind.Translation, applied.After.Patch!.Kind);
        switcher.ToOriginal(Game());
        var after = Snapshot(_game);
        Assert.All(before, pair => Assert.True(pair.Value.SequenceEqual(after[pair.Key]), $"'{pair.Key}' mudou."));
    }

    [Fact]
    public void Backup_que_sumiu_deixa_o_estado_desconhecido_e_a_volta_recusa_sem_tocar_no_jogo()
    {
        Switcher().ToTranslated(Game(), cancellationToken: TestContext.Current.CancellationToken);
        var inGame = Snapshot(_game);
        Directory.Delete(Assert.Single(BackupFolders()), recursive: true);

        var state = LibraryTranslationReader.Read(Game());

        Assert.Equal(LibraryTranslationKind.Unknown, state.Kind);
        Assert.Contains("cópia de segurança", state.Reason, StringComparison.Ordinal);
        Assert.Throws<LibraryException>(() => Switcher().ToOriginal(Game()));
        var after = Snapshot(_game);
        Assert.All(inGame, pair => Assert.True(pair.Value.SequenceEqual(after[pair.Key])));
    }

    [Fact]
    public void Jogo_que_mudou_depois_da_injecao_recusa_a_volta_e_nada_e_gravado()
    {
        Switcher().ToTranslated(Game(), cancellationToken: TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(_game, Package), "atualizado pela loja");

        Assert.Throws<InjectionException>(() => Switcher().ToOriginal(Game()));

        Assert.Equal("atualizado pela loja", File.ReadAllText(Path.Combine(_game, Package)));
    }

    [Fact]
    public void Conferencia_travada_nao_grava_nada_no_jogo()
    {
        var before = Snapshot(_game);
        var units = _project.LoadText("a001").Select((u, i) => i == 0 ? u with { Translation = string.Empty, Status = TranslationStatus.Translated } : u).ToList();
        _project.SaveText("a001", units);
        var switcher = new LibraryTranslationSwitcher(new GameTester([_engine], new NoStarter(), QaEngine.Default, _clock));

        Assert.Throws<QaBlockedException>(() => switcher.ToTranslated(Game(), cancellationToken: TestContext.Current.CancellationToken));

        var after = Snapshot(_game);
        Assert.All(before, pair => Assert.True(pair.Value.SequenceEqual(after[pair.Key])));
        Assert.Empty(BackupFolders());
        Assert.Equal(LibraryTranslationKind.Original, LibraryTranslationReader.Read(Game()).Kind);
    }

    private sealed class NoStarter : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory) => throw new InvalidOperationException("Trocar não abre o jogo.");
    }

    private sealed class CountingStarter : IGameProcessStarter
    {
        public int Calls { get; private set; }

        public void Start(string executablePath, string workingDirectory) => Calls++;
    }
}
