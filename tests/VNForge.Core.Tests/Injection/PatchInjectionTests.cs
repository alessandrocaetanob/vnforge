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

    private void SeedBuild()
    {
        File.WriteAllText(Path.Combine(_game, YsbinYpf), "original-ysbin");
        File.WriteAllText(Path.Combine(_project.BuildDirectory, YsbinYpf), "traduzido-ysbin");
    }

    [Fact]
    public void Executavel_desconhecido_e_recusado_sem_backup_e_sem_gravar()
    {
        File.WriteAllText(ExePath, "MZ outro jogo");

        var erro = Assert.Throws<InjectionException>(() => GameInjector.PlanPatch(_project, new FakePatch()));

        Assert.Contains("recusado", erro.Message);
        Assert.Contains("nada foi gravado", erro.Message);
        Assert.Contains(FakePatch.RefusalReason, erro.Message);
        Assert.Equal("MZ outro jogo", ReadExe());
        Assert.Empty(Directory.GetDirectories(_project.BackupDirectory));
        Assert.False(Directory.Exists(StagingDirectory));
    }

    [Fact]
    public void Executavel_ausente_e_recusado()
    {
        File.Delete(ExePath);

        var erro = Assert.Throws<InjectionException>(() => GameInjector.PlanPatch(_project, new FakePatch()));

        Assert.Contains(FakePatch.RefusalReason, erro.Message);
        Assert.Empty(Directory.GetDirectories(_project.BackupDirectory));
    }

    [Fact]
    public void Plano_nao_grava_nada_no_jogo()
    {
        var plan = GameInjector.PlanPatch(_project, new FakePatch());

        var file = Assert.Single(plan.Injection.Files);
        Assert.Equal(GameExe, file.RelativePath);
        Assert.True(file.ReplacesExisting);
        Assert.Equal(OriginalExe, ReadExe());
        Assert.Empty(Directory.GetDirectories(_project.BackupDirectory));
        Assert.Equal(PatchedExe, File.ReadAllText(file.BuildPath));
    }

    [Fact]
    public void Aplicar_faz_backup_do_original_e_grava_o_executavel_alterado()
    {
        var result = ApplyPatch(Injector());

        Assert.Equal(GameExe, Assert.Single(result.Written));
        Assert.Equal(PatchedExe, ReadExe());
        var backup = Assert.Single(Directory.GetDirectories(_project.BackupDirectory));
        Assert.Equal(backup, result.BackupDirectory);
        Assert.Equal(OriginalExe, File.ReadAllText(Path.Combine(backup, GameExe)));
        Assert.True(File.Exists(Path.Combine(backup, BackupManifest.FileName)));
    }

    [Fact]
    public void Aplicar_nao_deixa_copia_do_executavel_na_pasta_de_trabalho()
    {
        ApplyPatch(Injector());

        Assert.False(Directory.Exists(StagingDirectory));
    }

    [Fact]
    public void Descartar_o_plano_apaga_a_copia_de_trabalho_e_deixa_o_jogo_como_estava()
    {
        var plan = GameInjector.PlanPatch(_project, new FakePatch());

        GameInjector.DiscardPatch(plan);

        Assert.False(Directory.Exists(StagingDirectory));
        Assert.Equal(OriginalExe, ReadExe());
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
    public void Patch_ja_aplicado_nao_faz_outro_backup_nem_regrava()
    {
        ApplyPatch(Injector());

        var plan = GameInjector.PlanPatch(_project, new FakePatch());
        var result = Injector().InjectPatch(plan);

        Assert.Empty(plan.Injection.Files);
        Assert.Equal(1, plan.Injection.UnchangedCount);
        Assert.Null(result.BackupDirectory);
        Assert.Single(Directory.GetDirectories(_project.BackupDirectory));
        Assert.Equal(PatchedExe, ReadExe());
    }

    [Fact]
    public void Aplicar_o_patch_duas_vezes_e_restaurar_continua_devolvendo_o_original()
    {
        ApplyPatch(Injector());
        ApplyPatch(Injector());

        Injector().Restore(GameInjector.PlanRestore(_project));

        Assert.Equal(OriginalExe, ReadExe());
    }

    [Fact]
    public void Restore_depois_de_patch_e_inject_devolve_o_exe_e_os_pacotes()
    {
        SeedBuild();
        ApplyPatch(Injector());
        _clock.Advance(TimeSpan.FromMinutes(1));
        Injector().Inject(GameInjector.Plan(_project));

        var result = Injector().Restore(GameInjector.PlanRestore(_project));

        Assert.Equal([GameExe, YsbinYpf], result.Restored.Order(StringComparer.Ordinal));
        Assert.Equal(OriginalExe, ReadExe());
        Assert.Equal("original-ysbin", File.ReadAllText(Path.Combine(_game, YsbinYpf)));
    }

    [Fact]
    public void Restore_depois_de_inject_e_patch_devolve_o_exe_e_os_pacotes()
    {
        SeedBuild();
        Injector().Inject(GameInjector.Plan(_project));
        _clock.Advance(TimeSpan.FromMinutes(1));
        ApplyPatch(Injector());

        var result = Injector().Restore(GameInjector.PlanRestore(_project));

        Assert.Equal([GameExe, YsbinYpf], result.Restored.Order(StringComparer.Ordinal));
        Assert.Equal(OriginalExe, ReadExe());
        Assert.Equal("original-ysbin", File.ReadAllText(Path.Combine(_game, YsbinYpf)));
    }

    [Fact]
    public void Gravacao_que_falha_deixa_o_executavel_como_estava()
    {
        var injector = Injector(new FailingGameWrites(_game));

        var erro = Assert.Throws<InjectionException>(() => ApplyPatch(injector));

        Assert.Contains("O jogo voltou ao estado original", erro.Message);
        Assert.Equal(OriginalExe, ReadExe());
        Assert.False(Directory.Exists(StagingDirectory));
    }

    [Fact]
    public void Executavel_que_muda_depois_do_plano_nao_e_sobrescrito()
    {
        var plan = GameInjector.PlanPatch(_project, new FakePatch());
        File.WriteAllText(ExePath, "MZ atualizado por fora");

        var erro = Assert.Throws<InjectionException>(() => Injector().InjectPatch(plan));

        Assert.Contains("mudou depois da conferência", erro.Message);
        Assert.Equal("MZ atualizado por fora", ReadExe());
    }

    [Fact]
    public void Caminho_do_patch_fora_da_pasta_do_jogo_e_recusado()
    {
        var erro = Assert.Throws<InjectionException>(() => GameInjector.PlanPatch(_project, new FakePatch("../fora.exe")));

        Assert.Contains("Caminho recusado", erro.Message);
    }

    [Fact]
    public void Patch_sem_arquivos_e_recusado()
    {
        Assert.Throws<InjectionException>(() => GameInjector.PlanPatch(_project, new FakePatch(files: [])));
    }

    /// <summary>Troca <c>original-exe</c> por <c>patched--exe</c> no arquivo; reconhece os dois estados.</summary>
    private sealed class FakePatch(string file = GameExe, IReadOnlyList<string>? files = null) : IGamePatch
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

    private sealed class FailingGameWrites(string gameDirectory) : IInjectionFileSystem
    {
        private readonly PhysicalInjectionFileSystem _real = new();
        private bool _failed;

        public void CopyFile(string source, string destination)
        {
            if (!_failed && destination.StartsWith(gameDirectory, StringComparison.OrdinalIgnoreCase))
            {
                _failed = true;
                throw new IOException("disco cheio simulado");
            }

            _real.CopyFile(source, destination);
        }

        public void DeleteFile(string path) => _real.DeleteFile(path);
    }
}
