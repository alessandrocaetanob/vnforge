using VNForge.Core.Injection;
using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public sealed class GameLauncherTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly ProjectFolder _project;
    private readonly Recorder _starter = new();

    public GameLauncherTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(Path.Combine(_game, "bin"));
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game, "jogo.exe"),
                new EngineConfig("yuris"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("fold"),
                AiConfig.Default));
        _project.SaveState(new PipelineState([], new Dictionary<string, StageState>(), _game));
    }

    public void Dispose() => _temp.Dispose();

    private void UseExe(string exe) => _project.SaveConfig(_project.LoadConfig() with { Game = new GameConfig(_game, exe) });

    [Theory]
    [InlineData("jogo.exe")]
    [InlineData("JOGO.EXE")]
    [InlineData("bin/vn.exe")]
    [InlineData(@"bin\vn.exe")]
    public void Aceita_exe_dentro_da_pasta_do_jogo(string exe)
    {
        UseExe(exe);
        var path = Path.Combine(_game, exe.Replace('\\', '/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "MZ");

        var launched = new GameLauncher(_starter).Launch(_project);

        Assert.Equal(Path.GetFullPath(path), launched);
        Assert.Equal(_game, _starter.WorkingDirectory);
    }

    [Fact]
    public void Recusa_game_path_absoluto_fora_da_pasta_registrada_mesmo_com_exe_valido()
    {
        var system = Path.Combine(_temp.Path, "system32");
        Directory.CreateDirectory(system);
        File.WriteAllText(Path.Combine(system, "cmd.exe"), "MZ");
        _project.SaveConfig(_project.LoadConfig() with { Game = new GameConfig(system, "cmd.exe") });

        var erro = Assert.Throws<VNForgeException>(() => new GameLauncher(_starter).Launch(_project));

        Assert.Contains("game.path", erro.Message);
        Assert.Contains("registrou", erro.Message);
        Assert.Null(_starter.Executable);
    }

    [Fact]
    public void Recusa_game_path_relativo_com_ponto_ponto_que_cai_fora_da_pasta_registrada()
    {
        var outro = Path.Combine(_temp.Path, "outro");
        Directory.CreateDirectory(outro);
        File.WriteAllText(Path.Combine(outro, "jogo.exe"), "MZ");
        _project.SaveConfig(_project.LoadConfig() with { Game = new GameConfig("../outro", "jogo.exe") });

        var erro = Assert.Throws<VNForgeException>(() => new GameLauncher(_starter).Launch(_project));

        Assert.Contains("game.path", erro.Message);
        Assert.Null(_starter.Executable);
    }

    [Fact]
    public void Aceita_game_path_relativo_com_ponto_ponto_que_volta_a_pasta_registrada()
    {
        File.WriteAllText(Path.Combine(_game, "jogo.exe"), "MZ");
        _project.SaveConfig(_project.LoadConfig() with { Game = new GameConfig("../jogo/", "jogo.exe") });

        var launched = new GameLauncher(_starter).Launch(_project);

        Assert.Equal(Path.Combine(_game, "jogo.exe"), launched);
    }

    [Fact]
    public void Recusa_projeto_sem_pasta_de_jogo_registrada()
    {
        File.WriteAllText(Path.Combine(_game, "jogo.exe"), "MZ");
        _project.SaveState(PipelineState.Empty);

        var erro = Assert.Throws<VNForgeException>(() => new GameLauncher(_starter).Launch(_project));

        Assert.Contains("vnf extract", erro.Message);
        Assert.Null(_starter.Executable);
    }

    [Fact]
    public void Aceita_pasta_registrada_so_pelo_manifesto_de_um_backup()
    {
        File.WriteAllText(Path.Combine(_game, "jogo.exe"), "MZ");
        File.WriteAllText(Path.Combine(_game, "ysbin.ypf"), "original");
        Directory.CreateDirectory(_project.BuildDirectory);
        File.WriteAllText(Path.Combine(_project.BuildDirectory, "ysbin.ypf"), "traduzido");
        new GameInjector().Inject(GameInjector.Plan(_project));
        _project.SaveState(PipelineState.Empty);

        var launched = new GameLauncher(_starter).Launch(_project);

        Assert.Equal(Path.Combine(_game, "jogo.exe"), launched);
    }

    [Fact]
    public void Aceita_extensao_exe_em_qualquer_caixa()
    {
        File.WriteAllText(Path.Combine(_game, "Jogo.EXE"), "MZ");
        UseExe("Jogo.EXE");

        Assert.Equal(Path.Combine(_game, "Jogo.EXE"), new GameLauncher(_starter).Launch(_project));
    }

    [Fact]
    public void Recusa_caminho_absoluto_mesmo_que_o_arquivo_exista()
    {
        var outside = Path.Combine(_temp.Path, "fora.exe");
        File.WriteAllText(outside, "MZ");
        UseExe(outside);

        var erro = Assert.Throws<VNForgeException>(() => new GameLauncher(_starter).Launch(_project));

        Assert.Contains("game.exe", erro.Message);
        Assert.Contains("pasta do jogo", erro.Message);
        Assert.Null(_starter.Executable);
    }

    [Theory]
    [InlineData("../fora.exe")]
    [InlineData(@"..\fora.exe")]
    [InlineData("bin/../../fora.exe")]
    public void Recusa_exe_que_sai_da_pasta_do_jogo(string exe)
    {
        File.WriteAllText(Path.Combine(_temp.Path, "fora.exe"), "MZ");
        UseExe(exe);

        var erro = Assert.Throws<VNForgeException>(() => new GameLauncher(_starter).Launch(_project));

        Assert.Contains("pasta do jogo", erro.Message);
        Assert.Null(_starter.Executable);
    }

    [Theory]
    [InlineData("instalar.bat")]
    [InlineData("atalho.lnk")]
    [InlineData("script.cmd")]
    [InlineData("jogo")]
    public void Recusa_o_que_nao_e_exe(string exe)
    {
        File.WriteAllText(Path.Combine(_game, exe), "x");
        UseExe(exe);

        var erro = Assert.Throws<VNForgeException>(() => new GameLauncher(_starter).Launch(_project));

        Assert.Contains(".exe", erro.Message);
        Assert.Null(_starter.Executable);
    }

    [Fact]
    public void Exe_ausente_continua_dizendo_o_caminho_procurado()
    {
        var erro = Assert.Throws<GameExecutableNotFoundException>(() => new GameLauncher(_starter).Launch(_project));

        Assert.Equal(Path.Combine(_game, "jogo.exe"), erro.ExecutablePath);
    }

    private sealed class Recorder : IGameProcessStarter
    {
        public string? Executable { get; private set; }

        public string? WorkingDirectory { get; private set; }

        public void Start(string executablePath, string workingDirectory)
        {
            Executable = executablePath;
            WorkingDirectory = workingDirectory;
        }
    }
}
