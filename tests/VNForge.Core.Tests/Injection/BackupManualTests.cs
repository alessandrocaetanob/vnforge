using VNForge.Core.Injection;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Injection;

public sealed class BackupManualTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly string _external;
    private readonly ProjectFolder _project;
    private readonly ClockStub _clock = new(new DateTimeOffset(2026, 10, 24, 10, 20, 0, TimeSpan.Zero));

    public BackupManualTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        _external = Path.Combine(_temp.Path, "disco-externo");
        Directory.CreateDirectory(_game);
        Directory.CreateDirectory(_external);
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "Minha VN"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game, "jogo.exe"),
                new EngineConfig("yuris"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("fold"),
                AiConfig.Default));
        Write(_game, "ysbin.ypf", "original-ysbin");
        Write(_game, "bgm.ypf", "original-bgm");
        Write(_game, "jogo.exe", "original-exe");
        Write(_project.BuildDirectory, "ysbin.ypf", "traduzido-ysbin");
        Write(_project.TextDirectory, "cena01.json", "{\"linhas\":[]}");
    }

    public void Dispose() => _temp.Dispose();

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private GameInjector Injector() => new(_clock);

    /// <summary>Cada arquivo da pasta do jogo com o conteúdo em bytes: a prova de "igual byte a byte".</summary>
    private Dictionary<string, byte[]> SnapshotOfGame() =>
        Directory.EnumerateFiles(_game, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(_game, f), File.ReadAllBytes);

    // R1: o backup à mão guarda o original e o projeto na pasta escolhida; depois de uma injeção, apagar o backup dela e restaurar do à mão devolve o jogo igual.
    [Fact]
    public void R1_backup_a_mao_em_pasta_escolhida_restaura_o_jogo_byte_a_byte()
    {
        var original = SnapshotOfGame();
        _project.WithBackups(new BackupLocations(_external));
        var injection = Injector().Inject(GameInjector.Plan(_project));
        Assert.Equal(Path.Combine(_external, "Minha VN"), Path.GetDirectoryName(injection.BackupDirectory));
        _clock.Advance(TimeSpan.FromMinutes(5));

        var result = Injector().BackUpNow(_project, GameInjector.PlanBackup(_project), cancellationToken: TestContext.Current.CancellationToken);

        var manual = Assert.Single(GameInjector.ListBackups(_project), b => b.IsManual);
        Assert.Equal(result.BackupDirectory, manual.Directory);
        Assert.Equal(Path.Combine(_external, "Minha VN"), manual.Root);
        Assert.Equal("original-ysbin", File.ReadAllText(Path.Combine(manual.Directory, "ysbin.ypf")));
        Assert.Equal("{\"linhas\":[]}", File.ReadAllText(Path.Combine(manual.Directory, "projeto", "text", "cena01.json")));
        Assert.False(Directory.Exists(Path.Combine(manual.Directory, "projeto", "build")));
        Assert.Equal("traduzido-ysbin", File.ReadAllText(Path.Combine(_game, "ysbin.ypf")));

        Directory.Delete(injection.BackupDirectory!, recursive: true);
        Injector().Restore(GameInjector.PlanRestore(_project));

        var restored = SnapshotOfGame();
        Assert.Equal(original.Keys.Order(), restored.Keys.Order());
        Assert.All(original, pair => Assert.Equal(pair.Value, restored[pair.Key]));
    }

    // R2: trocar a pasta dos backups não perde nem esconde os que já existem, e a restauração acha o backup na pasta onde ele está.
    [Fact]
    public void R2_trocar_a_pasta_mantem_os_backups_antigos_na_lista_e_na_restauracao()
    {
        var original = SnapshotOfGame();
        var injection = Injector().Inject(GameInjector.Plan(_project));
        Assert.Equal(_project.BackupDirectory, Path.GetDirectoryName(injection.BackupDirectory));

        var library = new GameLibrary();
        library.SetBackupFolder(_external);
        _project.WithBackups(LibraryFile.Parse(LibraryFile.Serialize(library)).BackupLocations);
        _clock.Advance(TimeSpan.FromMinutes(5));
        var manual = Injector().BackUpNow(_project, GameInjector.PlanBackup(_project), cancellationToken: TestContext.Current.CancellationToken);

        var other = Path.Combine(_temp.Path, "outro-disco");
        Directory.CreateDirectory(other);
        library.SetBackupFolder(other);
        _project.WithBackups(library.BackupLocations);

        var listed = GameInjector.ListBackups(_project).Select(b => b.Directory).ToList();
        Assert.Equal([manual.BackupDirectory, injection.BackupDirectory!], listed);
        Assert.Equal([GameLibrary.NormalizeFolder(_external)], library.PreviousBackupFolders);

        Injector().Restore(GameInjector.PlanRestore(_project));
        Assert.All(original, pair => Assert.Equal(pair.Value, File.ReadAllBytes(Path.Combine(_game, pair.Key))));
    }

    private sealed class ClockStub(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
