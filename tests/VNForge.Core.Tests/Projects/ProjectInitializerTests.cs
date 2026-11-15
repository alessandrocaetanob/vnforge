using VNForge.Core.Engines;
using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R2")]
public sealed class ProjectInitializerTests : IDisposable
{
    private const string GameExe = "Jogo.exe";

    private readonly TempFolder _temp = new();
    private readonly string _game;

    public ProjectInitializerTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, GameExe), "MZ");
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
        Assert.Equal(GameExe, saved.Game.Exe);
        Assert.Equal(_game.Replace(Path.DirectorySeparatorChar, '/'), saved.Game.Path);
        Assert.Equal(new LanguagesConfig("ja", "pt-BR"), saved.Languages);
        Assert.Equal(new EncodingConfig("fold"), saved.Encoding);
    }

    [Fact]
    public void Estrategia_de_acentuacao_invalida_da_erro_sem_criar_o_projeto()
    {
        Assert.Throws<ProjectConfigException>(
            () => ProjectInitializer.Initialize(ProjectDir(), _game, [Matching()], new ProjectInitOptions(EncodingStrategy: "inexistente")));

        Assert.False(File.Exists(Path.Combine(ProjectDir(), ProjectFolder.ConfigFileName)));
    }

    [Fact]
    public void Inspect_de_pasta_inexistente_ou_sem_engine_da_erro_em_portugues()
    {
        var missing = Assert.Throws<ProjectInitException>(() => ProjectInitializer.Inspect(Path.Combine(_temp.Path, "nada"), [Matching()]));
        Assert.Contains("não existe", missing.Message);

        var unknown = Assert.Throws<ProjectInitException>(() => ProjectInitializer.Inspect(_game, [new FakeEngine(EngineDetection.NotDetected)]));
        Assert.Contains("Engine não reconhecida", unknown.Message);
    }

    private sealed class TitledEngine : IEngine, IGameTitleSource
    {
        public string Id => "titled";

        public EngineDetection Detect(string gameDir) => EngineDetection.Matched("parece titled");

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];

        public GameTitleFile? ReadTitle(string unpackedDirectory) => null;

        public string? ReadTitleFromGame(string gameDir)
        {
            var path = Path.Combine(gameDir, "titulo.cfg");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        public byte[] WithTitle(GameTitleFile file, string title, VNForge.Core.Text.ITextEncoder encoder) => [];
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
