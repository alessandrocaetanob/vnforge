using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Injection;

public sealed class GameInjectorTests : IDisposable
{
    private const string NewFile = "novo.txt";
    private const string GameExe = "jogo.exe";
    private const string OriginalYsbinDir = "original-ysbin";
    private const string YsbinYpf = "ysbin.ypf";
    private const string BgmYpf = "bgm.ypf";
    private const string OriginalBgmDir = "original-bgm";
    private const string TranslatedYsbinDir = "traduzido-ysbin";
    private const string TranslatedBgmDir = "traduzido-bgm";

    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly ProjectFolder _project;
    private readonly ClockStub _clock = new(new DateTimeOffset(2026, 10, 23, 10, 20, 0, TimeSpan.Zero));

    public GameInjectorTests()
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
    }

    public void Dispose() => _temp.Dispose();

    private GameInjector Injector(IInjectionFileSystem? fileSystem = null) => new(_clock, fileSystem);

    private void Game(string relative, string content) => Write(_game, relative, content);

    private void Build(string relative, string content) => Write(_project.BuildDirectory, relative, content);

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string ReadGame(string relative) => File.ReadAllText(Path.Combine(_game, relative));

    private InjectionResult InjectAll(GameInjector injector) => injector.Inject(injector.Plan(_project));

    private RestoreResult RestoreLatest(GameInjector injector) => injector.Restore(injector.PlanRestore(_project));

    private void SeedGameAndBuild()
    {
        Game(YsbinYpf, OriginalYsbinDir);
        Game(BgmYpf, OriginalBgmDir);
        Game(GameExe, "original-exe");
        Build(YsbinYpf, TranslatedYsbinDir);
        Build(BgmYpf, TranslatedBgmDir);
    }

    private void AssertGameIsOriginal()
    {
        Assert.Equal(OriginalYsbinDir, ReadGame(YsbinYpf));
        Assert.Equal(OriginalBgmDir, ReadGame(BgmYpf));
        Assert.Equal("original-exe", ReadGame(GameExe));
    }

    [Fact]
    public void Plan_sem_pasta_build_pede_o_vnf_build()
    {
        Directory.Delete(_project.BuildDirectory);

        var erro = Assert.Throws<InjectionException>(() => Injector().Plan(_project));

        Assert.Contains("build/ não existe", erro.Message);
        Assert.Contains("vnf build", erro.Message);
    }

    [Fact]
    public void Plan_com_build_vazio_pede_o_vnf_build()
    {
        var erro = Assert.Throws<InjectionException>(() => Injector().Plan(_project));

        Assert.Contains("build/ está vazia", erro.Message);
    }

    [Fact]
    public void Plan_com_pasta_do_jogo_inexistente_diz_qual()
    {
        Build(YsbinYpf, "x");
        Directory.Delete(_game);

        var erro = Assert.Throws<InjectionException>(() => Injector().Plan(_project));

        Assert.Contains("A pasta do jogo não existe", erro.Message);
    }

    [Fact]
    public void Plan_aceita_game_path_relativo_a_pasta_do_projeto()
    {
        _project.SaveConfig(_project.LoadConfig() with { Game = new GameConfig("../jogo", GameExe) });
        Game(YsbinYpf, OriginalYsbinDir);
        Build(YsbinYpf, TranslatedYsbinDir);

        var plan = Injector().Plan(_project);

        Assert.Equal(_game, plan.GameDirectory);
        Assert.Single(plan.Files);
    }

    [Fact]
    public void Plan_nao_escreve_nada()
    {
        SeedGameAndBuild();

        Injector().Plan(_project);

        AssertGameIsOriginal();
        Assert.Empty(Directory.EnumerateFileSystemEntries(_project.BackupDirectory));
    }

    [Fact]
    public void Inject_copia_so_os_originais_trocados_para_o_backup_datado_e_grava_o_build()
    {
        SeedGameAndBuild();

        var result = InjectAll(Injector());

        var backup = Path.Combine(_project.BackupDirectory, "2026-10-23_1020");
        Assert.Equal(backup, result.BackupDirectory);
        Assert.Equal([BgmYpf, YsbinYpf], result.Written);
        Assert.Equal(OriginalYsbinDir, File.ReadAllText(Path.Combine(backup, YsbinYpf)));
        Assert.Equal(OriginalBgmDir, File.ReadAllText(Path.Combine(backup, BgmYpf)));
        Assert.False(File.Exists(Path.Combine(backup, GameExe)));
        Assert.True(File.Exists(Path.Combine(backup, BackupManifest.FileName)));
        Assert.Equal(TranslatedYsbinDir, ReadGame(YsbinYpf));
        Assert.Equal(TranslatedBgmDir, ReadGame(BgmYpf));
        Assert.Equal("original-exe", ReadGame(GameExe));
        Assert.Empty(Directory.EnumerateFiles(_game, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Inject_trata_subpastas_e_arquivos_novos()
    {
        Game("data/cg.ypf", "original-cg");
        Build("data/cg.ypf", "traduzido-cg");
        Build("data/novo.txt", "novo");

        var result = InjectAll(Injector());

        Assert.Equal("traduzido-cg", ReadGame("data/cg.ypf"));
        Assert.Equal("novo", ReadGame("data/novo.txt"));
        Assert.False(File.Exists(Path.Combine(result.BackupDirectory!, "data", NewFile)));
    }

    [Fact]
    public void Inject_ignora_arquivo_do_build_identico_ao_do_jogo_e_nao_cria_backup_sem_mudanca()
    {
        Game(YsbinYpf, "igual");
        Build(YsbinYpf, "igual");

        var plan = Injector().Plan(_project);
        var result = Injector().Inject(plan);

        Assert.Empty(plan.Files);
        Assert.Equal(1, plan.UnchangedCount);
        Assert.Null(result.BackupDirectory);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_project.BackupDirectory));
    }

    [Fact]
    public void Inject_na_mesma_hora_nao_sobrescreve_o_backup_anterior()
    {
        SeedGameAndBuild();
        var first = InjectAll(Injector());
        Build(YsbinYpf, "traduzido-ysbin-v2");

        var second = InjectAll(Injector());

        Assert.EndsWith("2026-10-23_1020", first.BackupDirectory);
        Assert.EndsWith("2026-10-23_1020-02", second.BackupDirectory);
    }

    [Fact]
    public void Segunda_injecao_sem_restore_guarda_o_original_de_verdade_e_nao_o_build_anterior()
    {
        SeedGameAndBuild();
        InjectAll(Injector());
        _clock.Now = _clock.Now.AddMinutes(5);
        Build(YsbinYpf, "traduzido-ysbin-v2");

        var second = InjectAll(Injector());

        Assert.Equal(OriginalYsbinDir, File.ReadAllText(Path.Combine(second.BackupDirectory!, YsbinYpf)));
        Assert.Equal("traduzido-ysbin-v2", ReadGame(YsbinYpf));

        RestoreLatest(Injector());

        Assert.Equal(OriginalYsbinDir, ReadGame(YsbinYpf));
    }

    [Fact]
    public void Plan_recusa_arquivo_do_build_com_o_nome_do_manifesto()
    {
        Build(BackupManifest.FileName, "{}");

        var erro = Assert.Throws<InjectionException>(() => Injector().Plan(_project));

        Assert.Contains("reservado", erro.Message);
    }

    [Fact]
    public void Terceira_injecao_acha_o_original_num_backup_que_nao_e_o_mais_recente()
    {
        SeedGameAndBuild();
        InjectAll(Injector());
        _clock.Now = _clock.Now.AddMinutes(1);
        Build(YsbinYpf, OriginalYsbinDir);
        Build(BgmYpf, "traduzido-bgm-v2");
        File.Delete(Path.Combine(_project.BuildDirectory, YsbinYpf));
        InjectAll(Injector());
        _clock.Now = _clock.Now.AddMinutes(1);
        Build(YsbinYpf, "traduzido-ysbin-v3");

        var third = InjectAll(Injector());

        Assert.Equal(OriginalYsbinDir, File.ReadAllText(Path.Combine(third.BackupDirectory!, YsbinYpf)));
        RestoreLatest(Injector());
        Assert.Equal(OriginalYsbinDir, ReadGame(YsbinYpf));
    }

    [Fact]
    public void Restore_devolve_na_pasta_onde_a_injecao_ocorreu_mesmo_com_game_path_mudado()
    {
        SeedGameAndBuild();
        InjectAll(Injector());
        var outro = Path.Combine(_temp.Path, "outro");
        Directory.CreateDirectory(outro);
        File.WriteAllText(Path.Combine(outro, YsbinYpf), "do-outro-jogo");
        _project.SaveConfig(_project.LoadConfig() with { Game = new GameConfig(outro, GameExe) });

        var result = RestoreLatest(Injector());

        Assert.Equal(_game, result.GameDirectory);
        AssertGameIsOriginal();
        Assert.Equal("do-outro-jogo", File.ReadAllText(Path.Combine(outro, YsbinYpf)));
    }

    [Fact]
    public void Restore_nao_apaga_arquivo_criado_pela_injecao_que_mudou_depois()
    {
        SeedGameAndBuild();
        Build(NewFile, "novo");
        InjectAll(Injector());
        Game(NewFile, "editado-depois");

        var result = RestoreLatest(Injector());

        Assert.Equal([NewFile], result.Kept);
        Assert.Equal("editado-depois", ReadGame(NewFile));
        AssertGameIsOriginal();
    }

    [Fact]
    public void Arquivo_novo_que_aparece_entre_o_plano_e_a_injecao_aborta_sem_sobrescrever()
    {
        Build(NewFile, "do-build");
        var injector = Injector();
        var plan = injector.Plan(_project);
        Game(NewFile, "criado-por-outro");

        var erro = Assert.Throws<InjectionException>(() => injector.Inject(plan));

        Assert.Contains("mudou depois da conferência", erro.Message);
        Assert.Equal("criado-por-outro", ReadGame(NewFile));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_project.BackupDirectory));
    }

    [Fact]
    public void Falha_no_meio_da_injecao_devolve_o_jogo_ao_estado_original()
    {
        SeedGameAndBuild();
        var disco = new FlakyFileSystem(_game) { FailOnGameWrite = 2 };

        var erro = Assert.Throws<InjectionException>(() => InjectAll(Injector(disco)));

        Assert.Contains("O jogo voltou ao estado original", erro.Message);
        Assert.Contains("'ysbin.ypf'", erro.Message);
        AssertGameIsOriginal();
        Assert.Empty(Directory.EnumerateFiles(_game, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Falha_que_impede_o_desfazer_deixa_o_jogo_restauravel_pelo_restore()
    {
        SeedGameAndBuild();
        var disco = new FlakyFileSystem(_game) { FailOnGameWrite = 2, StayBroken = true };

        var erro = Assert.Throws<InjectionException>(() => InjectAll(Injector(disco)));

        Assert.Contains("vnf restore", erro.Message);
        Assert.Equal(TranslatedBgmDir, ReadGame(BgmYpf));

        var restored = RestoreLatest(Injector());

        Assert.Contains(BgmYpf, restored.Restored);
        AssertGameIsOriginal();
    }

    [Fact]
    public void Falha_no_backup_nao_grava_nada_no_jogo_e_nao_deixa_backup_incompleto()
    {
        SeedGameAndBuild();
        var disco = new FlakyFileSystem(_game) { FailOnBackupWrite = 2, BackupDirectory = _project.BackupDirectory };

        var erro = Assert.Throws<InjectionException>(() => InjectAll(Injector(disco)));

        Assert.Contains("Nada foi gravado no jogo", erro.Message);
        AssertGameIsOriginal();
        Assert.Empty(Directory.EnumerateFileSystemEntries(_project.BackupDirectory));
    }

    [Fact]
    public void Jogo_que_muda_entre_o_plano_e_o_backup_aborta_antes_de_gravar()
    {
        SeedGameAndBuild();
        var injector = Injector();
        var plan = injector.Plan(_project);
        Game(YsbinYpf, "atualizado-pelo-jogo");

        var erro = Assert.Throws<InjectionException>(() => injector.Inject(plan));

        Assert.Contains("mudou depois da conferência", erro.Message);
        Assert.Equal("atualizado-pelo-jogo", ReadGame(YsbinYpf));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_project.BackupDirectory));
        Assert.Equal(OriginalBgmDir, ReadGame(BgmYpf));
    }

    [Fact]
    public void Restore_devolve_os_originais_e_apaga_o_que_a_injecao_criou()
    {
        SeedGameAndBuild();
        Build(NewFile, "novo");
        InjectAll(Injector());

        var result = RestoreLatest(Injector());

        AssertGameIsOriginal();
        Assert.False(File.Exists(Path.Combine(_game, NewFile)));
        Assert.Equal([BgmYpf, YsbinYpf], result.Restored);
        Assert.Equal([NewFile], result.Removed);
    }

    [Fact]
    public void Restore_pode_ser_repetido()
    {
        SeedGameAndBuild();
        InjectAll(Injector());
        RestoreLatest(Injector());

        RestoreLatest(Injector());

        AssertGameIsOriginal();
    }

    [Fact]
    public void Restore_sem_backup_diz_que_nao_ha_o_que_restaurar()
    {
        var erro = Assert.Throws<InjectionException>(() => Injector().PlanRestore(_project));

        Assert.Contains("Não há backup", erro.Message);
    }

    [Fact]
    public void Restore_ignora_pasta_de_backup_sem_manifesto()
    {
        SeedGameAndBuild();
        InjectAll(Injector());
        Directory.CreateDirectory(Path.Combine(_project.BackupDirectory, "2099-01-01_0000"));

        RestoreLatest(Injector());

        AssertGameIsOriginal();
    }

    [Fact]
    public void Restore_com_copia_do_backup_adulterada_recusa_sem_gravar()
    {
        SeedGameAndBuild();
        var result = InjectAll(Injector());
        File.WriteAllText(Path.Combine(result.BackupDirectory!, BgmYpf), "adulterado");

        var erro = Assert.Throws<InjectionException>(() => RestoreLatest(Injector()));

        Assert.Contains("nada foi gravado", erro.Message);
        Assert.Equal(TranslatedYsbinDir, ReadGame(YsbinYpf));
        Assert.Equal(TranslatedBgmDir, ReadGame(BgmYpf));
    }

    [Fact]
    public void Restore_com_manifesto_ilegivel_diz_qual_arquivo()
    {
        SeedGameAndBuild();
        var result = InjectAll(Injector());
        File.WriteAllText(Path.Combine(result.BackupDirectory!, BackupManifest.FileName), "{ não é json");

        var erro = Assert.Throws<InjectionException>(() => Injector().PlanRestore(_project));

        Assert.Contains(BackupManifest.FileName, erro.Message);
    }

    [Fact]
    public void Restore_recusa_manifesto_com_caminho_que_sai_da_pasta_do_jogo()
    {
        SeedGameAndBuild();
        var result = InjectAll(Injector());
        var manifestPath = Path.Combine(result.BackupDirectory!, BackupManifest.FileName);
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace("\"bgm.ypf\"", "\"../fora.txt\""));

        var erro = Assert.Throws<InjectionException>(() => RestoreLatest(Injector()));

        Assert.Contains("fora.txt", erro.Message);
        Assert.False(File.Exists(Path.Combine(_temp.Path, "fora.txt")));
    }

    private sealed class ClockStub(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FlakyFileSystem(string gameDirectory) : IInjectionFileSystem
    {
        private readonly PhysicalInjectionFileSystem _real = new();
        private int _gameWrites;
        private int _backupWrites;
        private bool _broken;

        public int FailOnGameWrite { get; init; } = int.MaxValue;

        public int FailOnBackupWrite { get; init; } = int.MaxValue;

        public string? BackupDirectory { get; init; }

        public bool StayBroken { get; init; }

        public void CopyFile(string source, string destination)
        {
            if (IsUnder(gameDirectory, destination) && Fails(ref _gameWrites, FailOnGameWrite))
            {
                throw new IOException("disco cheio simulado");
            }

            if (BackupDirectory is not null && IsUnder(BackupDirectory, destination) && Fails(ref _backupWrites, FailOnBackupWrite))
            {
                throw new IOException("disco cheio simulado");
            }

            _real.CopyFile(source, destination);
        }

        public void DeleteFile(string path)
        {
            if (_broken)
            {
                throw new IOException("disco cheio simulado");
            }

            _real.DeleteFile(path);
        }

        private static bool IsUnder(string directory, string path) =>
            path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        private bool Fails(ref int counter, int failOn)
        {
            counter++;
            if (_broken || counter == failOn)
            {
                _broken = StayBroken;
                return true;
            }

            return false;
        }
    }
}
