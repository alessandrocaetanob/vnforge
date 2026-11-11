using System.Security.Cryptography;
using System.Text;
using VNForge.Core.Engines;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Injection;

/// <summary>O patch de executável passa pelo mesmo backup, conferência de hash e restauração da injeção (E5.5 · #65).</summary>
public sealed class PatchInjectionTests : IDisposable
{
    private const string GameExe = "jogo.exe";
    private const string YsbinYpf = "ysbin.ypf";
    private const string OriginalExe = "MZ original-exe";
    private const string PatchedExe = "MZ patched--exe";

    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly ProjectFolder _project;
    private readonly ClockStub _clock = new(new DateTimeOffset(2026, 10, 25, 10, 20, 0, TimeSpan.Zero));

    public PatchInjectionTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(_game);
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game, GameExe),
                new EngineConfig("yuris"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("fold"),
                AiConfig.Default));
        File.WriteAllText(Path.Combine(_game, GameExe), OriginalExe);
    }

    public void Dispose() => _temp.Dispose();

    private GameInjector Injector(IInjectionFileSystem? fileSystem = null) => new(_clock, fileSystem);

    private string ExePath => Path.Combine(_game, GameExe);

    private string ReadExe() => File.ReadAllText(ExePath);

    private string ExeHash() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ExePath)));

    private string StagingDirectory => Path.Combine(_project.CacheDirectory, "patch", FakePatch.PatchId);

    private InjectionResult ApplyPatch(GameInjector injector, FakePatch? patch = null) =>
        injector.InjectPatch(GameInjector.PlanPatch(_project, patch ?? new FakePatch()));

    [Fact]
    public void Projeto_dentro_da_pasta_do_jogo_e_recusado_sem_criar_a_copia_de_trabalho()
    {
        var jogo = _temp.Path;
        File.WriteAllText(Path.Combine(jogo, GameExe), OriginalExe);
        _project.SaveConfig(_project.LoadConfig() with { Game = new GameConfig(jogo, GameExe) });

        var erro = Assert.Throws<InjectionException>(() => GameInjector.PlanPatch(_project, new FakePatch()));

        Assert.Contains("fica dentro da pasta do jogo", erro.Message);
        Assert.Contains("fora da injeção com backup", erro.Message);
        Assert.False(Directory.Exists(StagingDirectory));
        Assert.Equal(OriginalExe, File.ReadAllText(Path.Combine(jogo, GameExe)));
        Assert.Empty(Directory.GetDirectories(_project.BackupDirectory));
    }

    [Fact]
    public void Restore_devolve_o_executavel_original_byte_a_byte()
    {
        var originalHash = ExeHash();
        ApplyPatch(Injector());
        Assert.NotEqual(originalHash, ExeHash());

        var result = Injector().Restore(GameInjector.PlanRestore(_project));

        Assert.Equal([GameExe], result.Restored);
        Assert.Equal(OriginalExe, ReadExe());
        Assert.Equal(originalHash, ExeHash());
    }

    [Fact]
    public void Apply_que_recusa_o_arquivo_vira_recusa_do_patch_e_apaga_a_copia_de_trabalho()
    {
        var erro = Assert.Throws<InjectionException>(() => GameInjector.PlanPatch(_project, new FakePatch(applyThrows: true)));

        Assert.Contains("recusado", erro.Message);
        Assert.Contains("apply recusou", erro.Message);
        Assert.False(Directory.Exists(StagingDirectory));
        Assert.Equal(OriginalExe, ReadExe());
        Assert.Empty(Directory.GetDirectories(_project.BackupDirectory));
    }

    [Fact]
    public void Patch_sem_arquivos_e_recusado()
    {
        Assert.Throws<InjectionException>(() => GameInjector.PlanPatch(_project, new FakePatch(files: [])));
    }

    /// <summary>Troca <c>original-exe</c> por <c>patched--exe</c> no arquivo; reconhece os dois estados.</summary>
    private sealed class FakePatch(string file = GameExe, IReadOnlyList<string>? files = null, bool applyThrows = false) : IGamePatch
    {
        public const string PatchId = "fake-exe";
        public const string RefusalReason = "não é o executável esperado.";

        public string Id => PatchId;

        public string Description => "Troca o texto do executável de teste.";

        public IReadOnlyList<string> AffectedFiles { get; } = files ?? [file];

        public bool CanApply(string gameDir, out string? reason)
        {
            var path = Path.Combine(gameDir, file);
            var text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
            reason = text.Contains("original-exe", StringComparison.Ordinal) || text.Contains("patched--exe", StringComparison.Ordinal)
                ? null
                : RefusalReason;
            return reason is null;
        }

        public void Apply(string gameDir)
        {
            if (applyThrows)
            {
                throw new InvalidOperationException("apply recusou o arquivo.");
            }

            var path = Path.Combine(gameDir, file);
            File.WriteAllText(path, File.ReadAllText(path).Replace("original-exe", "patched--exe", StringComparison.Ordinal));
        }
    }

    private sealed class ClockStub(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
