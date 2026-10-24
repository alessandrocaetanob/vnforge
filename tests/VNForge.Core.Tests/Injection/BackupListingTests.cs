using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Injection;

public sealed class BackupListingTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly ProjectFolder _project;
    private readonly ClockStub _clock = new(new DateTimeOffset(2026, 10, 22, 23, 16, 0, TimeSpan.Zero));

    public BackupListingTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(_game);
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game, "jogo.exe"),
                new EngineConfig("yuris"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("fold"),
                AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void Inject(string ysbin, string extra = "")
    {
        Write(_game, "ysbin.ypf", "original");
        Write(_project.BuildDirectory, "ysbin.ypf", ysbin);
        if (extra.Length > 0)
        {
            Write(_project.BuildDirectory, "novo.txt", extra);
        }

        new GameInjector(_clock).Inject(GameInjector.Plan(_project));
    }

    [Fact]
    public void Sem_pasta_de_backup_a_lista_e_vazia()
    {
        Assert.Empty(GameInjector.ListBackups(_project));
    }

    [Fact]
    public void Lista_o_backup_com_data_tamanho_e_arquivos_do_manifesto()
    {
        Inject("traduzido", extra: "novo");

        var backup = Assert.Single(GameInjector.ListBackups(_project));

        Assert.Equal("2026-10-22_2316", backup.Name);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 23, 16, 0, TimeSpan.Zero), backup.Manifest!.CreatedAt);
        Assert.Equal("original".Length, backup.SizeBytes);
        Assert.Null(backup.Problem);
        Assert.False(backup.Restored);
        Assert.Equal(["ysbin.ypf"], backup.OriginalFiles);
        Assert.Equal(["novo.txt"], backup.CreatedFiles);
    }

    [Fact]
    public void O_tamanho_nao_conta_o_manifesto()
    {
        Inject("traduzido");
        var directory = Directory.GetDirectories(_project.BackupDirectory).Single();
        var manifestLength = new FileInfo(Path.Combine(directory, BackupManifest.FileName)).Length;

        var backup = Assert.Single(GameInjector.ListBackups(_project));

        Assert.True(manifestLength > 0);
        Assert.Equal(new FileInfo(Path.Combine(directory, "ysbin.ypf")).Length, backup.SizeBytes);
    }

    [Fact]
    public void Do_mais_novo_para_o_mais_antigo_e_inclui_o_ja_restaurado()
    {
        Inject("traduzido");
        var injector = new GameInjector(_clock);
        injector.Restore(GameInjector.PlanRestore(_project));
        _clock.Advance(TimeSpan.FromDays(2));
        Inject("traduzido de novo");

        var backups = GameInjector.ListBackups(_project);

        Assert.Equal(["2026-10-24_2316", "2026-10-22_2316"], backups.Select(b => b.Name));
        Assert.False(backups[0].Restored);
        Assert.True(backups[1].Restored);
    }

    [Fact]
    public void Manifesto_ilegivel_aparece_com_o_problema_em_vez_de_derrubar_a_lista()
    {
        Inject("traduzido");
        var broken = Path.Combine(_project.BackupDirectory, "2026-10-01_0900");
        Write(broken, BackupManifest.FileName, "{ isto não é json");
        Write(broken, "ysbin.ypf", "guardado");

        var backups = GameInjector.ListBackups(_project);

        Assert.Equal(["2026-10-22_2316", "2026-10-01_0900"], backups.Select(b => b.Name));
        Assert.Null(backups[0].Problem);
        Assert.Null(backups[1].Manifest);
        Assert.Contains("manifesto", backups[1].Problem);
        Assert.Equal("guardado".Length, backups[1].SizeBytes);
    }

    [Fact]
    public void Pasta_sem_manifesto_nao_e_um_backup()
    {
        Write(_project.BackupDirectory, Path.Combine("qualquer", "coisa.txt"), "x");

        Assert.Empty(GameInjector.ListBackups(_project));
    }

    private sealed class ClockStub(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
