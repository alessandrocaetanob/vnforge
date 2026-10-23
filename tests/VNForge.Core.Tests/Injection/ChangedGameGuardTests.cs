using System.Security.Cryptography;
using System.Text;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Injection;

public sealed class ChangedGameGuardTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly ProjectFolder _project;
    private readonly ClockStub _clock = new(new DateTimeOffset(2026, 10, 23, 10, 20, 0, TimeSpan.Zero));

    public ChangedGameGuardTests()
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

    private GameInjector Injector() => new(_clock);

    private void Game(string relative, string content) => Write(_game, relative, content);

    private void Build(string relative, string content) => Write(_project.BuildDirectory, relative, content);

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static OriginalFile Original(string relative, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new OriginalFile(relative, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private void RecordExtraction(params OriginalFile[] originals) =>
        _project.SaveState(new PipelineState(originals, new Dictionary<string, StageState>()));

    private void SeedExtractedGame()
    {
        Game("ysbin.ypf", "original-ysbin");
        Game("bgm.ypf", "original-bgm");
        Build("ysbin.ypf", "traduzido-ysbin");
        RecordExtraction(Original("ysbin.ypf", "original-ysbin"), Original("bgm.ypf", "original-bgm"));
    }

    [Fact]
    public void Plan_com_jogo_igual_ao_da_extracao_segue_normalmente()
    {
        SeedExtractedGame();

        var plan = Injector().Plan(_project);

        Assert.Equal(["ysbin.ypf"], plan.Files.Select(f => f.RelativePath));
    }

    [Fact]
    public void Plan_recusa_arquivo_do_jogo_alterado_depois_da_extracao_e_diz_qual()
    {
        SeedExtractedGame();
        Game("bgm.ypf", "atualizado-pelo-patch");

        var erro = Assert.Throws<InjectionException>(() => Injector().Plan(_project));

        Assert.Contains("bgm.ypf", erro.Message);
        Assert.DoesNotContain("ysbin.ypf", erro.Message);
        Assert.Contains("extra", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_lista_todos_os_arquivos_alterados_e_os_ausentes()
    {
        SeedExtractedGame();
        Game("ysbin.ypf", "versao-nova");
        File.Delete(Path.Combine(_game, "bgm.ypf"));

        var erro = Assert.Throws<InjectionException>(() => Injector().Plan(_project));

        Assert.Contains("ysbin.ypf", erro.Message);
        Assert.Contains("bgm.ypf", erro.Message);
        Assert.Contains("ausente", erro.Message);
    }

    [Fact]
    public void Recusa_nao_cria_backup_nem_toca_no_jogo()
    {
        SeedExtractedGame();
        Game("bgm.ypf", "atualizado-pelo-patch");

        Assert.Throws<InjectionException>(() => Injector().Plan(_project));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_project.BackupDirectory));
        Assert.Equal("original-ysbin", File.ReadAllText(Path.Combine(_game, "ysbin.ypf")));
    }

    [Fact]
    public void Segunda_injecao_nao_confunde_o_build_anterior_com_jogo_alterado()
    {
        SeedExtractedGame();
        var injector = Injector();
        injector.Inject(injector.Plan(_project));
        Build("ysbin.ypf", "traduzido-v2");
        _clock.Now = _clock.Now.AddMinutes(5);

        var plan = injector.Plan(_project);

        Assert.Equal(["ysbin.ypf"], plan.Files.Select(f => f.RelativePath));
    }

    [Fact]
    public void Plan_depois_do_restore_aceita_o_jogo_original()
    {
        SeedExtractedGame();
        var injector = Injector();
        injector.Inject(injector.Plan(_project));
        injector.Restore(injector.PlanRestore(_project));

        var plan = injector.Plan(_project);

        Assert.Equal(["ysbin.ypf"], plan.Files.Select(f => f.RelativePath));
    }

    [Fact]
    public void Jogo_sem_estado_de_extracao_nao_e_verificado()
    {
        Game("ysbin.ypf", "original-ysbin");
        Build("ysbin.ypf", "traduzido-ysbin");

        var plan = Injector().Plan(_project);

        Assert.Single(plan.Files);
    }

    private sealed class ClockStub(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
