using System.Text;
using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

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
    public void Devolve_os_cinco_passos_na_ordem_e_cada_um_com_tempo()
    {
        Translate("a001", 0, "Hello");
        var started = new List<GameTestStep>();

        var result = Tester().Run(_project, steps: new Collect<GameTestStep>(started.Add), cancellationToken: TestContext.Current.CancellationToken);

        GameTestStep[] order = [GameTestStep.Check, GameTestStep.Build, GameTestStep.Backup, GameTestStep.Inject, GameTestStep.Open];
        Assert.Equal(order, started);
        Assert.Equal(order, result.Steps.Select(s => s.Step));
        Assert.All(result.Steps, s => Assert.True(s.Elapsed > TimeSpan.Zero, $"{s.Step} sem tempo"));
    }

    [Fact]
    public void Grava_o_estado_do_jogo_e_o_ultimo_teste()
    {
        Translate("a001", 0, "Hello");

        var result = Run();

        var state = GameStateFile.Load(_project);
        Assert.Equal(GameTestKind.Translation, state.InGame?.Kind);
        Assert.Equal(_clock.GetUtcNow(), state.InGame!.AppliedAt, TimeSpan.FromSeconds(1));
        Assert.Equal(Path.GetFileName(Assert.Single(BackupFolders())), state.InGame.BackupDirectory);
        Assert.False(string.IsNullOrEmpty(state.InGame.TextFingerprint));
        Assert.Equal(result.Steps.Select(s => s.Step), state.LastTest!.Steps.Select(s => s.Step));
        Assert.Equal(GameTestKind.Translation, state.LastTest.Kind);
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
    public void Aviso_da_Conferencia_so_passa_com_a_opcao()
    {
        Translate("a001", 0, "Olá  mundo");

        Assert.Throws<QaBlockedException>(() => Run(qa: QaEngine.Default));
        Assert.Empty(BackupFolders());

        Run(new GameTestOptions(AllowWarnings: true), QaEngine.Default);

        Assert.Single(BackupFolders());
    }

    [Fact]
    public void Teste_de_acentos_injeta_so_a_frase_fixa_e_voltar_ao_original_desfaz()
    {
        Translate("a002", 0, "Chovia muito.");
        var before = File.ReadAllBytes(GamePackage);

        var result = Run(new GameTestOptions(AccentTest: true));

        Assert.Equal(GameTestKind.AccentTest, result.Kind);
        Assert.Equal($"dialogue|{GameTester.AccentPhrase}\nname|Ana\u0001narration|Chovia.", File.ReadAllText(GamePackage));
        Assert.Equal(GameTestKind.AccentTest, GameStateFile.Load(_project).InGame?.Kind);
        Assert.All("çãéô", c => Assert.Contains(c, GameTester.AccentPhrase));

        Tester().ReturnToOriginal(_project);

        Assert.Equal(before, File.ReadAllBytes(GamePackage));
        Assert.Null(GameStateFile.Load(_project).InGame);
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
    public void Teste_de_acentos_nao_depende_da_conferencia_do_resto_do_projeto()
    {
        Translate("a001", 1, string.Empty);

        var result = Run(new GameTestOptions(AccentTest: true), QaEngine.Default);

        Assert.Equal(GameTestKind.AccentTest, result.Kind);
        Assert.Contains(GameTester.AccentPhrase, File.ReadAllText(GamePackage));
    }

    [Fact]
    public void So_uma_cena_deixa_as_outras_como_no_original()
    {
        Translate("a001", 0, "Hello");
        Translate("a002", 0, "Rain");

        Run(new GameTestOptions(OnlyScript: "a002"));

        Assert.Equal("dialogue|Olá\nname|Ana\u0001narration|Rain", File.ReadAllText(GamePackage));
        Assert.Equal("a002", GameStateFile.Load(_project).InGame?.ScriptKey);
    }

    [Fact]
    public void Cena_inexistente_recusa_antes_de_gravar()
    {
        var erro = Assert.Throws<VNForgeException>(() => Run(new GameTestOptions(OnlyScript: "naoexiste")));

        Assert.Contains("naoexiste", erro.Message);
        Assert.Equal(OriginalPackage, File.ReadAllText(GamePackage));
        Assert.Empty(BackupFolders());
    }

    [Fact]
    public void Teste_de_acentos_e_uma_cena_nao_combinam()
    {
        Assert.Throws<VNForgeException>(() => Run(new GameTestOptions(OnlyScript: "a001", AccentTest: true)));

        Assert.Empty(BackupFolders());
    }

    [Fact]
    public void Executavel_ausente_recusa_antes_de_gravar_no_jogo()
    {
        Translate("a001", 0, "Hello");
        File.Delete(Path.Combine(_game, GameExe));

        Assert.Throws<GameExecutableNotFoundException>(() => Run());

        Assert.Equal(OriginalPackage, File.ReadAllText(GamePackage));
        Assert.Empty(BackupFolders());
    }

    [Fact]
    public void Sem_abrir_o_jogo_injeta_e_nao_dispara_o_executavel()
    {
        Translate("a001", 0, "Hello");

        var result = Run(new GameTestOptions(OpenGame: false));

        Assert.Null(result.Executable);
        Assert.Null(_starter.Executable);
        Assert.Equal("dialogue|Hello\nname|Ana\u0001narration|Chovia.", File.ReadAllText(GamePackage));
    }

    [Fact]
    public void Jogo_que_nao_abre_deixa_o_estado_do_que_foi_injetado()
    {
        Translate("a001", 0, "Hello");
        _starter.Failure = new VNForgeException("sem permissão");

        Assert.Throws<GameOpenException>(() => Run());

        Assert.NotNull(GameStateFile.Load(_project).InGame);
        Assert.Null(GameStateFile.Load(_project).LastTest);
    }

    [Fact]
    public void Repetir_o_teste_com_o_jogo_igual_nao_cria_outro_backup_e_mantem_o_primeiro()
    {
        Translate("a001", 0, "Hello");
        Run();
        var first = GameStateFile.Load(_project).InGame!.BackupDirectory;

        Run();

        Assert.Single(BackupFolders());
        Assert.Equal(first, GameStateFile.Load(_project).InGame!.BackupDirectory);
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

    [Fact]
    public void A_trava_e_solta_depois_de_uma_excecao_dentro_do_teste()
    {
        Translate("a001", 0, string.Empty);

        Assert.Throws<QaBlockedException>(() => Run(qa: QaEngine.Default));

        ProjectLock.Acquire(_project).Dispose();
        Translate("a001", 0, "Hello");
        Run();
    }

    [Fact]
    public void A_trava_e_solta_depois_de_um_cancelamento_dentro_do_teste()
    {
        Translate("a001", 0, "Hello");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Tester().Run(_project, cancellationToken: cancelled.Token));

        Assert.Empty(BackupFolders());
        ProjectLock.Acquire(_project).Dispose();
        Run();
    }

    [Fact]
    public void Falha_que_nao_e_de_compartilhamento_diz_que_nao_deu_para_preparar_a_pasta()
    {
        Directory.Delete(_project.CacheDirectory, recursive: true);
        File.WriteAllText(_project.CacheDirectory, "isto era para ser uma pasta");

        var erro = Assert.Throws<ProjectLockUnavailableException>(() => ProjectLock.Acquire(_project));

        Assert.Equal("Não foi possível preparar a pasta do projeto para mexer no jogo.", erro.Message);
        Assert.IsNotType<ProjectBusyException>(erro);
    }

    [Fact]
    public void Sem_permissao_para_abrir_o_arquivo_da_trava_tambem_nao_vira_ocupado()
    {
        Directory.CreateDirectory(Path.Combine(_project.CacheDirectory, "game.lock"));

        Assert.Throws<ProjectLockUnavailableException>(() => ProjectLock.Acquire(_project));
    }

    [Fact]
    public void A_trava_do_projeto_so_solta_no_fim_da_operacao()
    {
        var held = ProjectLock.Acquire(_project);

        Assert.Throws<ProjectBusyException>(() => ProjectLock.Acquire(_project));
        held.Dispose();

        ProjectLock.Acquire(_project).Dispose();
    }

    [Fact]
    public void Injecao_que_falha_e_e_desfeita_nao_deixa_estado_do_jogo_nem_backup_pendente()
    {
        Translate("a001", 0, "Hello");
        ProjectBuilder.Build(_project, [_engine], qa: new QaEngine([]), cancellationToken: TestContext.Current.CancellationToken);
        GameStateFile.Save(_project, GameStateFile.Empty with { InGame = new GameInGame(GameTestKind.Translation, _clock.GetUtcNow(), "antigo", null, null) });

        var failing = new GameInjector(_clock, new FailsFirstCopy());
        Assert.Throws<InjectionException>(() => GameStateFile.Inject(_project, failing, GameInjector.Plan(_project), GameTestKind.Translation, null, _clock));

        Assert.Null(GameStateFile.Load(_project).InGame);
        Assert.Equal(OriginalPackage, File.ReadAllText(GamePackage));
        Assert.All(GameInjector.ListBackups(_project), backup => Assert.True(backup.Restored));
        Assert.Throws<InjectionException>(() => GameInjector.PlanRestore(_project));
    }

    [Fact]
    public void Injecao_recusada_antes_de_gravar_mantem_o_estado_que_ja_valia()
    {
        Translate("a001", 0, "Hello");
        ProjectBuilder.Build(_project, [_engine], qa: new QaEngine([]), cancellationToken: TestContext.Current.CancellationToken);
        var saved = new GameInGame(GameTestKind.AccentTest, _clock.GetUtcNow(), "2026-10-22_2316", null, null);
        GameStateFile.Save(_project, GameStateFile.Empty with { InGame = saved });
        var plan = GameInjector.Plan(_project);
        File.WriteAllText(GamePackage, "o jogo foi atualizado no meio");

        Assert.Throws<InjectionException>(() => GameStateFile.Inject(_project, new GameInjector(_clock), plan, GameTestKind.Translation, null, _clock));

        Assert.Equal(saved, GameStateFile.Load(_project).InGame);
    }

    [Fact]
    public void Inject_de_fora_do_teste_grava_o_estado_do_jogo_e_so_quando_copiou_algo()
    {
        Translate("a001", 0, "Hello");
        ProjectBuilder.Build(_project, [_engine], qa: new QaEngine([]), cancellationToken: TestContext.Current.CancellationToken);

        GameStateFile.Inject(_project, new GameInjector(_clock), GameInjector.Plan(_project), GameTestKind.Translation, null, _clock);
        var first = GameStateFile.Load(_project).InGame;
        GameStateFile.Inject(_project, new GameInjector(_clock), GameInjector.Plan(_project), GameTestKind.Translation, null, _clock);

        Assert.NotNull(first?.BackupDirectory);
        Assert.Equal(first, GameStateFile.Load(_project).InGame);
    }

    [Fact]
    public void Teste_sem_nada_a_copiar_nao_regrava_o_estado_com_backup_nulo()
    {
        Translate("a001", 0, "Hello");
        Run();
        var first = GameStateFile.Load(_project).InGame;

        Run();

        Assert.Equal(first, GameStateFile.Load(_project).InGame);
        Assert.NotNull(GameStateFile.Load(_project).InGame?.BackupDirectory);
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
