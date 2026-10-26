using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Engines;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>#342: o teste no jogo aplica o patch do executável quando os acentos são "ajuste no jogo" (cp1252).</summary>
public sealed class GameTesterPatchTests : IDisposable
{
    private const string GameExe = "Jogo.exe";
    private const string OriginalExe = "MZ original-exe";
    private const string PatchedExe = "MZ patched--exe";

    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly ProjectFolder _project;
    private readonly FakeEngine _engine = new();
    private readonly FakeStarter _starter = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero)) { AutoAdvanceAmount = TimeSpan.FromMilliseconds(10) };
    private int _patchRequests;

    public GameTesterPatchTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "a001.fake"), "dialogue|Olá\nname|Ana");
        File.WriteAllText(Path.Combine(_game, "a002.fake"), "narration|Chovia.");
        File.WriteAllText(ExePath, OriginalExe);
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
        File.WriteAllText(PackagePath, "dialogue|Olá\nname|Ana\u0001narration|Chovia.");
        foreach (var key in new[] { "a001", "a002" })
        {
            var script = _engine.Scripts[0].Extract(new ScriptFile(key + ".fake", File.ReadAllBytes(Path.Combine(_project.GameCacheDirectory, FakeEngine.PackageName, key + ".fake"))), new FakeContext());
            _project.SaveText(key, script.Units);
        }

        _engine.ExePatches = executable =>
        {
            _patchRequests++;
            return [new FakeExePatch(executable)];
        };
        Translate("a001", 0, "Hello");
    }

    public void Dispose() => _temp.Dispose();

    private string ExePath => Path.Combine(_game, GameExe);

    private string PackagePath => Path.Combine(_game, FakeEngine.PackageName);

    private string PatchStaging => Path.Combine(_project.CacheDirectory, "patch", FakeExePatch.PatchId);

    private GameTester Tester() => new([_engine], _starter, new QaEngine([]), _clock);

    private GameTestResult Run(GameTestOptions? options = null) =>
        Tester().Run(_project, options, cancellationToken: TestContext.Current.CancellationToken);

    private void Translate(string key, int index, string text)
    {
        var units = _project.LoadText(key).Select((u, i) => i == index ? u with { Translation = text, Status = TranslationStatus.Translated } : u).ToList();
        _project.SaveText(key, units);
    }

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private string[] BackupFolders() =>
        Directory.Exists(_project.BackupDirectory) ? Directory.GetDirectories(_project.BackupDirectory) : [];

    [Fact]
    public void Projeto_cp1252_sai_do_teste_com_o_exe_trocado_e_o_backup_no_manifesto_com_o_hash_conferido()
    {
        var originalHash = Sha256(ExePath);

        var result = Run();

        Assert.Equal(PatchedExe, File.ReadAllText(ExePath));
        var backup = Assert.Single(GameInjector.ListBackups(_project));
        var entry = Assert.Single(backup.Manifest!.Files, f => f.Path == GameExe);
        Assert.True(entry.Existed);
        Assert.Equal(originalHash, entry.OriginalSha256);
        Assert.Equal(Sha256(ExePath), entry.InjectedSha256);
        Assert.Equal(OriginalExe, File.ReadAllText(Path.Combine(backup.Directory, GameExe)));
        Assert.Contains(GameExe, result.Injection.Written);
        Assert.Contains(FakeEngine.PackageName, result.Injection.Written);
        Assert.Equal(ExePath, _starter.Executable);
        Assert.False(Directory.Exists(PatchStaging), "a cópia de trabalho do patch tem que ser apagada");
    }

    [Fact]
    public void Segunda_rodada_nao_grava_o_exe_de_novo_nem_cria_outro_backup()
    {
        Run();
        var backup = Assert.Single(BackupFolders());
        var exeWrittenAt = File.GetLastWriteTimeUtc(ExePath);

        var second = Run();

        Assert.Equal(backup, Assert.Single(BackupFolders()));
        Assert.Null(second.Injection.BackupDirectory);
        Assert.Empty(second.Injection.Written);
        Assert.Equal(exeWrittenAt, File.GetLastWriteTimeUtc(ExePath));
        Assert.Equal(PatchedExe, File.ReadAllText(ExePath));
    }

    [Fact]
    public void Jogo_que_ja_esta_com_o_patch_e_com_o_build_nao_grava_nada_nem_cria_backup()
    {
        File.WriteAllText(ExePath, PatchedExe);
        Run(new GameTestOptions(OpenGame: false));
        var backup = Assert.Single(BackupFolders());

        Assert.DoesNotContain(GameExe, GameInjector.ListBackups(_project).Single().Manifest!.Files.Select(f => f.Path));
        Run(new GameTestOptions(OpenGame: false));

        Assert.Equal(backup, Assert.Single(BackupFolders()));
        Assert.Equal(PatchedExe, File.ReadAllText(ExePath));
    }

    [Fact]
    public void Exe_que_o_patch_recusa_para_o_teste_antes_de_gravar_qualquer_arquivo_no_jogo()
    {
        File.WriteAllText(ExePath, "MZ outro programa");
        var package = File.ReadAllBytes(PackagePath);

        var erro = Assert.Throws<GamePatchRefusedException>(() => Run());

        Assert.Equal("MZ outro programa", File.ReadAllText(ExePath));
        Assert.Equal(package, File.ReadAllBytes(PackagePath));
        Assert.Empty(BackupFolders());
        Assert.Null(_starter.Executable);
        Assert.False(File.Exists(GameStateFile.PathFor(_project)));
        Assert.False(Directory.Exists(PatchStaging));
        Assert.Contains(FakeExePatch.RefusalReason, erro.Message);
        Assert.IsType<InjectionException>(erro.InnerException);
    }

    [Fact]
    public void Recusa_do_patch_vale_tambem_sem_abrir_o_jogo_e_no_teste_de_acentos()
    {
        File.WriteAllText(ExePath, "MZ outro programa");

        Assert.Throws<GamePatchRefusedException>(() => Run(new GameTestOptions(OpenGame: false)));
        Assert.Throws<GamePatchRefusedException>(() => Run(new GameTestOptions(AccentTest: true)));

        Assert.Empty(BackupFolders());
    }

    [Fact]
    public void Teste_de_acentos_tambem_aplica_o_patch()
    {
        var result = Run(new GameTestOptions(AccentTest: true));

        Assert.Equal(GameTestKind.AccentTest, result.Kind);
        Assert.Equal(PatchedExe, File.ReadAllText(ExePath));
        Assert.Contains(GameTester.AccentPhrase, File.ReadAllText(PackagePath));
    }

    [Fact]
    public void Projeto_sjis_deixa_o_exe_byte_a_byte_igual_e_nem_pede_o_patch()
    {
        _project.SaveConfig(_project.LoadConfig() with { Encoding = new EncodingConfig("sjis") });
        var before = File.ReadAllBytes(ExePath);

        Run();

        Assert.Equal(before, File.ReadAllBytes(ExePath));
        Assert.Equal(0, _patchRequests);
        var backup = Assert.Single(GameInjector.ListBackups(_project));
        Assert.DoesNotContain(GameExe, backup.Manifest!.Files.Select(f => f.Path));
    }

    [Fact]
    public void Engine_sem_patch_para_o_executavel_segue_sem_mexer_nele()
    {
        _engine.ExePatches = _ => [];
        var before = File.ReadAllBytes(ExePath);

        Run();

        Assert.Equal(before, File.ReadAllBytes(ExePath));
        Assert.Single(BackupFolders());
    }

    [Fact]
    public void Voltar_ao_jogo_original_devolve_o_exe_junto_com_o_pacote()
    {
        var exe = File.ReadAllBytes(ExePath);
        var package = File.ReadAllBytes(PackagePath);
        Run();
        Assert.NotEqual(exe, File.ReadAllBytes(ExePath));

        var restored = Tester().ReturnToOriginal(_project);

        Assert.Equal(exe, File.ReadAllBytes(ExePath));
        Assert.Equal(package, File.ReadAllBytes(PackagePath));
        Assert.Contains(GameExe, restored.Restored);
        Assert.Null(GameStateFile.Load(_project).InGame);
    }

    [Fact]
    public void Patch_aplicado_num_teste_antigo_sobrevive_a_um_teste_novo_e_a_restauracao_devolve_o_original()
    {
        var exe = File.ReadAllBytes(ExePath);
        Run();
        Translate("a001", 0, "Hi");

        Run();
        Tester().ReturnToOriginal(_project);

        Assert.Equal(exe, File.ReadAllBytes(ExePath));
    }

    /// <summary>Troca <c>original-exe</c> por <c>patched--exe</c> no executável; reconhece os dois estados e recusa o resto.</summary>
    private sealed class FakeExePatch(string executable) : IGamePatch
    {
        public const string PatchId = "exe-font";
        public const string RefusalReason = "o nome da fonte não aparece no executável";

        public string Id => PatchId;

        public string Description => "Troca o texto do executável de teste.";

        public IReadOnlyList<string> AffectedFiles { get; } = [executable];

        public bool CanApply(string gameDir, out string? reason)
        {
            var path = Path.Combine(gameDir, executable);
            var text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
            reason = text.Contains("original-exe", StringComparison.Ordinal) || text.Contains("patched--exe", StringComparison.Ordinal) ? null : RefusalReason;
            return reason is null;
        }

        public void Apply(string gameDir)
        {
            var path = Path.Combine(gameDir, executable);
            File.WriteAllText(path, File.ReadAllText(path).Replace("original-exe", "patched--exe", StringComparison.Ordinal));
        }
    }

    private sealed class FakeStarter : IGameProcessStarter
    {
        public string? Executable { get; private set; }

        public void Start(string executablePath, string workingDirectory) => Executable = executablePath;
    }
}
