using System.Text;
using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R1")]
public sealed class GameTesterTests : IDisposable
{
    private const string Package = FakeEngine.PackageName;
    private const string GameExe = "Jogo.exe";
    private const string OriginalPackage = "dialogue|Olá\nname|Ana\u0001narration|Chovia.";

    private const string OriginalAfterTest = "dialogue|Hello\nname|Ana\u0001narration|Chovia.";

    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly ProjectFolder _project;
    private readonly FakeEngine _engine = new();
    private readonly RecordingStarter _starter = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero)) { AutoAdvanceAmount = TimeSpan.FromMilliseconds(10) };

    public GameTesterTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "a001.fake"), "dialogue|Olá\nname|Ana");
        File.WriteAllText(Path.Combine(_game, "a002.fake"), "narration|Chovia.");
        File.WriteAllText(Path.Combine(_game, GameExe), "MZ");
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game, GameExe),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));
        _project.SaveState(new PipelineState([], new Dictionary<string, StageState>(), _game));
        _engine.Unpack(_game, _project.GameCacheDirectory, TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(_game, Package), OriginalPackage);
        foreach (var key in new[] { "a001", "a002" })
        {
            var script = _engine.Scripts[0].Extract(new Engines.ScriptFile(key + ".fake", File.ReadAllBytes(Path.Combine(_project.GameCacheDirectory, Package, key + ".fake"))), new FakeContext());
            _project.SaveText(key, script.Units);
        }
    }

    public void Dispose() => _temp.Dispose();

    private string GamePackage => Path.Combine(_game, Package);

    private GameTester Tester(QaEngine? qa = null) => new([_engine], _starter, qa ?? new QaEngine([]), _clock);

    private void Translate(string key, int index, string text)
    {
        var units = _project.LoadText(key).Select((u, i) => i == index ? u with { Translation = text, Status = TranslationStatus.Translated } : u).ToList();
        _project.SaveText(key, units);
    }

    private GameTestResult Run(GameTestOptions? options = null, QaEngine? qa = null) =>
        Tester(qa).Run(_project, options, cancellationToken: TestContext.Current.CancellationToken);

    private string[] BackupFolders() =>
        Directory.Exists(_project.BackupDirectory) ? Directory.GetDirectories(_project.BackupDirectory) : [];

    [Fact]
    public void Teste_completo_faz_backup_antes_de_gravar_injeta_e_abre_o_jogo()
    {
        Translate("a001", 0, "Hello");

        var result = Run();

        Assert.Equal("dialogue|Hello\nname|Ana\u0001narration|Chovia.", File.ReadAllText(GamePackage));
        var backup = Assert.Single(BackupFolders());
        Assert.Equal(OriginalPackage, File.ReadAllText(Path.Combine(backup, Package)));
        Assert.True(File.Exists(Path.Combine(backup, BackupManifest.FileName)));
        Assert.Equal(Path.Combine(_game, GameExe), _starter.Executable);
        Assert.Equal(_game, _starter.WorkingDirectory);
        Assert.Equal(Path.Combine(_game, GameExe), result.Executable);
        Assert.Equal(GameTestKind.Translation, result.Kind);
    }

    [Fact]
    public void Voltar_ao_jogo_original_restaura_byte_a_byte_e_limpa_o_estado()
    {
        var before = File.ReadAllBytes(GamePackage);
        Translate("a001", 0, "Hello");
        Run();
        Assert.NotEqual(before, File.ReadAllBytes(GamePackage));

        var restored = Tester().ReturnToOriginal(_project);

        Assert.Equal(before, File.ReadAllBytes(GamePackage));
        Assert.Contains(Package, restored.Restored);
        var state = GameStateFile.Load(_project);
        Assert.Null(state.InGame);
        Assert.NotNull(state.LastTest);
    }

    [Fact]
    public void Erros_da_Conferencia_recusam_sem_tocar_no_jogo_nem_criar_backup()
    {
        Translate("a001", 0, string.Empty);
        var untouched = File.ReadAllBytes(GamePackage);

        Assert.Throws<QaBlockedException>(() => Run(qa: QaEngine.Default));

        Assert.Equal(untouched, File.ReadAllBytes(GamePackage));
        Assert.Empty(BackupFolders());
        Assert.Null(_starter.Executable);
        Assert.False(File.Exists(GameStateFile.PathFor(_project)));
    }

    [Fact]
    public void Teste_de_acentos_com_falante_japones_passa_na_cp1252_com_o_nome_de_teste()
    {
        _project.SaveText("a001", [.. _project.LoadText("a001").Select((u, i) => i == 0 ? u with { Speaker = "犬井" } : u)]);

        var result = Run(new GameTestOptions(AccentTest: true), QaEngine.Default);

        Assert.Equal(GameTestKind.AccentTest, result.Kind);
        Assert.Equal(GameTester.AccentSpeaker, Assert.Single(_engine.LastRebuiltUnits, u => u.NeedsRewrite).Speaker);
    }

    [Fact]
    public void Falha_do_build_mantem_o_ultimo_teste_anterior()
    {
        Translate("a001", 0, "Hello");
        Run();
        var before = GameStateFile.Load(_project).LastTest!;
        _clock.Advance(TimeSpan.FromHours(1));

        Assert.Throws<VNForgeException>(() => Run(new GameTestOptions(OnlyScript: "naoexiste")));

        Assert.Equal(before.At, GameStateFile.Load(_project).LastTest!.At);
    }

    [Fact]
    public void Testar_de_novo_com_o_jogo_injetado_e_uma_edicao_no_meio_injeta_por_cima_sem_restaurar()
    {
        var before = File.ReadAllBytes(GamePackage);
        Translate("a001", 0, "Hello");
        Run();
        Translate("a001", 0, "Hi");

        Run();

        Assert.Equal("dialogue|Hi\nname|Ana\u0001narration|Chovia.", File.ReadAllText(GamePackage));
        Assert.All(BackupFolders(), backup => Assert.Equal(OriginalPackage, File.ReadAllText(Path.Combine(backup, Package))));
        Assert.Equal(2, BackupFolders().Length);

        Tester().ReturnToOriginal(_project);

        Assert.Equal(before, File.ReadAllBytes(GamePackage));
    }

    [Fact]
    public async Task Dois_testes_ao_mesmo_tempo_o_segundo_recusa_e_o_backup_do_primeiro_fica_intacto()
    {
        Translate("a001", 0, "Hello");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _starter.OnStart = () =>
        {
            entered.SetResult();
            proceed.Task.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        };

        var first = Task.Run(() => Run(), TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var backup = Assert.Single(BackupFolders());

        Assert.Throws<ProjectBusyException>(() => Run());
        Assert.Throws<ProjectBusyException>(() => Tester().ReturnToOriginal(_project));
        Assert.Equal(OriginalPackage, File.ReadAllText(Path.Combine(backup, Package)));
        Assert.True(File.Exists(Path.Combine(backup, BackupManifest.FileName)));

        proceed.SetResult();
        await first;

        Assert.Equal(backup, Assert.Single(BackupFolders()));
        Assert.Equal(OriginalPackage, File.ReadAllText(Path.Combine(backup, Package)));
        Assert.Equal(OriginalAfterTest, File.ReadAllText(GamePackage));
        Run(new GameTestOptions(OpenGame: false));
    }

    /// <summary>Falha na segunda cópia: a primeira é a do original para o backup, e a segunda é a gravação no jogo.</summary>
    private sealed class FailsFirstCopy : IInjectionFileSystem
    {
        private int _copies;

        public void CopyFile(string source, string destination)
        {
            if (++_copies == 2)
            {
                throw new IOException("disco cheio");
            }

            File.Copy(source, destination, overwrite: true);
        }

        public void DeleteFile(string path) => File.Delete(path);
    }

    private sealed class RecordingStarter : IGameProcessStarter
    {
        public Action? OnStart { get; set; }

        public string? Executable { get; private set; }

        public string? WorkingDirectory { get; private set; }

        public VNForgeException? Failure { get; set; }

        public void Start(string executablePath, string workingDirectory)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Executable = executablePath;
            WorkingDirectory = workingDirectory;
            OnStart?.Invoke();
        }
    }

    private sealed class Collect<T>(Action<T> add) : IProgress<T>
    {
        public void Report(T value) => add(value);
    }
}
