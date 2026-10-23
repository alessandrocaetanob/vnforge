using VNForge.Core.Engines;
using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public sealed class ProjectInitializerTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly string _game;

    public ProjectInitializerTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "Jogo.exe"), "MZ");
    }

    public void Dispose() => _temp.Dispose();

    private string ProjectDir(string name = "projeto") => Path.Combine(_temp.Path, name);

    private static FakeEngine Matching(params (string Key, string Value)[] details) =>
        new(EngineDetection.Matched("parece fake", details.ToDictionary(d => d.Key, d => d.Value)));

    [Fact]
    public void Grava_engine_versao_e_chave_do_detalhe_da_deteccao()
    {
        var engine = Matching(("ypfVersion", "500"), ("scriptKey", "0xD36FAC96"));

        var result = ProjectInitializer.Initialize(ProjectDir(), _game, [engine]);

        var saved = ProjectFolder.Open(ProjectDir()).LoadConfig();
        Assert.Equal(new EngineConfig("fake", 500, "0xD36FAC96"), saved.Engine);
        Assert.Equal(saved, result.Config);
        Assert.Equal("projeto", saved.Name);
        Assert.Equal("Jogo.exe", saved.Game.Exe);
        Assert.Equal(_game.Replace(Path.DirectorySeparatorChar, '/'), saved.Game.Path);
        Assert.Equal(new LanguagesConfig("ja", "pt-BR"), saved.Languages);
    }

    [Fact]
    public void Sem_versao_nem_chave_deixa_os_campos_nulos()
    {
        ProjectInitializer.Initialize(ProjectDir(), _game, [Matching()]);

        var engine = ProjectFolder.Open(ProjectDir()).LoadConfig().Engine;
        Assert.Equal(new EngineConfig("fake"), engine);
    }

    [Fact]
    public void Cria_as_pastas_do_projeto_e_nao_grava_nada_no_jogo()
    {
        ProjectInitializer.Initialize(ProjectDir(), _game, [Matching()]);

        var folder = ProjectFolder.Open(ProjectDir());
        Assert.True(Directory.Exists(folder.TextDirectory));
        Assert.True(Directory.Exists(folder.CacheDirectory));
        Assert.Equal(["Jogo.exe"], Directory.EnumerateFileSystemEntries(_game).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void Pasta_que_ja_tem_projeto_da_erro_e_nao_sobrescreve()
    {
        ProjectInitializer.Initialize(ProjectDir(), _game, [Matching(("ypfVersion", "500"))], new ProjectInitOptions(Name: "Original"));
        var before = File.ReadAllText(ProjectFolder.Open(ProjectDir()).ConfigPath);

        var error = Assert.Throws<ProjectConfigException>(
            () => ProjectInitializer.Initialize(ProjectDir(), _game, [Matching(("ypfVersion", "600"))], new ProjectInitOptions(Name: "Outro")));

        Assert.Contains("não foi sobrescrito", error.Message);
        Assert.Equal(before, File.ReadAllText(ProjectFolder.Open(ProjectDir()).ConfigPath));
    }

    [Fact]
    public void Engine_nao_reconhecida_da_erro_sem_criar_a_pasta()
    {
        var engine = new FakeEngine(EngineDetection.NotDetected);

        var error = Assert.Throws<ProjectInitException>(() => ProjectInitializer.Initialize(ProjectDir(), _game, [engine]));

        Assert.Contains("Engine não reconhecida", error.Message);
        Assert.False(Directory.Exists(ProjectDir()));
    }

    [Fact]
    public void Engine_nao_reconhecida_cita_os_avisos_da_deteccao()
    {
        var engine = new FakeEngine(new EngineDetection(false, string.Empty, new Dictionary<string, string> { ["warnings"] = "a.ypf: bloqueado" }));

        var error = Assert.Throws<ProjectInitException>(() => ProjectInitializer.Initialize(ProjectDir(), _game, [engine]));

        Assert.Contains("a.ypf: bloqueado", error.Message);
    }

    [Fact]
    public void Usa_a_primeira_engine_que_reconhece()
    {
        var first = new FakeEngine(EngineDetection.NotDetected, "primeira");
        var second = new FakeEngine(EngineDetection.Matched("ok"), "segunda");

        var result = ProjectInitializer.Initialize(ProjectDir(), _game, [first, second]);

        Assert.Equal("segunda", result.Config.Engine.Id);
    }

    [Fact]
    public void Pasta_do_jogo_inexistente_da_erro()
    {
        var error = Assert.Throws<ProjectInitException>(
            () => ProjectInitializer.Initialize(ProjectDir(), Path.Combine(_temp.Path, "nao-existe"), [Matching()]));

        Assert.Contains("A pasta do jogo não existe", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("projeto")]
    public void Projeto_dentro_da_pasta_do_jogo_da_erro(string subfolder)
    {
        var project = subfolder.Length == 0 ? _game : Path.Combine(_game, subfolder);

        var error = Assert.Throws<ProjectInitException>(() => ProjectInitializer.Initialize(project, _game, [Matching()]));

        Assert.Contains("não pode ficar dentro da pasta do jogo", error.Message);
        Assert.Equal(["Jogo.exe"], Directory.EnumerateFileSystemEntries(_game).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void Pasta_vizinha_com_o_mesmo_prefixo_do_jogo_nao_conta_como_dentro()
    {
        var project = Path.Combine(_temp.Path, "jogo-ptbr");

        ProjectInitializer.Initialize(project, _game, [Matching()]);

        Assert.True(File.Exists(Path.Combine(project, ProjectFolder.ConfigFileName)));
    }

    [Fact]
    public void Nome_informado_vale_mais_que_o_da_pasta()
    {
        var result = ProjectInitializer.Initialize(ProjectDir(), _game, [Matching()], new ProjectInitOptions(Name: " Minha VN "));

        Assert.Equal("Minha VN", result.Config.Name);
    }

    [Fact]
    public void Executavel_unico_ignora_desinstalador_e_maiusculas_da_extensao()
    {
        File.Delete(Path.Combine(_game, "Jogo.exe"));
        File.WriteAllText(Path.Combine(_game, "Vn.EXE"), "MZ");
        File.WriteAllText(Path.Combine(_game, "unins000.exe"), "MZ");

        var result = ProjectInitializer.Initialize(ProjectDir(), _game, [Matching()]);

        Assert.Equal("Vn.EXE", result.Config.Game.Exe);
    }

    [Fact]
    public void Sem_executavel_pede_a_opcao_exe_e_nao_cria_o_projeto()
    {
        File.Delete(Path.Combine(_game, "Jogo.exe"));

        var error = Assert.Throws<ProjectInitException>(() => ProjectInitializer.Initialize(ProjectDir(), _game, [Matching()]));

        Assert.Contains("--exe", error.Message);
        Assert.False(Directory.Exists(ProjectDir()));
    }

    [Fact]
    public void Varios_executaveis_pedem_a_opcao_exe()
    {
        File.WriteAllText(Path.Combine(_game, "Outro.exe"), "MZ");

        var error = Assert.Throws<ProjectInitException>(() => ProjectInitializer.Initialize(ProjectDir(), _game, [Matching()]));

        Assert.Contains("Jogo.exe, Outro.exe", error.Message);
    }

    [Fact]
    public void Executavel_informado_pode_estar_em_subpasta_e_sai_com_barra_normal()
    {
        Directory.CreateDirectory(Path.Combine(_game, "bin"));
        File.WriteAllText(Path.Combine(_game, "bin", "Run.exe"), "MZ");

        var result = ProjectInitializer.Initialize(ProjectDir(), _game, [Matching()], new ProjectInitOptions(Exe: "bin\\Run.exe"));

        Assert.Equal("bin/Run.exe", result.Config.Game.Exe);
    }

    [Theory]
    [InlineData("nao-existe.exe")]
    [InlineData("../fora.exe")]
    public void Executavel_informado_que_nao_esta_no_jogo_da_erro(string exe)
    {
        File.WriteAllText(Path.Combine(_temp.Path, "fora.exe"), "MZ");

        var error = Assert.Throws<ProjectInitException>(
            () => ProjectInitializer.Initialize(ProjectDir(), _game, [Matching()], new ProjectInitOptions(Exe: exe)));

        Assert.Contains("não existe dentro da pasta do jogo", error.Message);
        Assert.False(Directory.Exists(ProjectDir()));
    }

    private sealed class FakeEngine(EngineDetection detection, string id = "fake") : IEngine
    {
        public string Id { get; } = id;

        public EngineDetection Detect(string gameDir) => detection;

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];
    }
}
