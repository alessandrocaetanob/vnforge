using VNForge.Core.Engines;
using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

/// <summary>#536: os nomes de personagem que o jogo traz, lidos antes de o projeto existir (o assistente casa com o VNDB).</summary>
public sealed class GameCharacterNamesTests : IDisposable
{
    private readonly TempFolder temp = new();
    private readonly string game;

    public GameCharacterNamesTests()
    {
        game = Path.Combine(temp.Path, "jogo");
        Directory.CreateDirectory(game);
    }

    public void Dispose() => temp.Dispose();

    private static GameInspection Inspect(IEngine engine) => new(engine, EngineDetection.Matched("fake"), []);

    [Fact]
    public void Devolve_os_nomes_na_ordem_da_extracao_sem_repetir_e_ignora_o_que_nao_e_nome()
    {
        File.WriteAllText(Path.Combine(game, "b001.fake"), "dialogue|Oi\nname|Aoi\nname|Ren");
        File.WriteAllText(Path.Combine(game, "a001.fake"), "name|Kenta\nname|Aoi\nnarration|Chovia.");

        var names = ProjectInitializer.ReadGameCharacterNames(Inspect(new FakeEngine()), game, TestContext.Current.CancellationToken);

        Assert.Equal(["Kenta", "Aoi", "Ren"], names);
    }

    [Fact]
    public void Nao_grava_nada_na_pasta_do_jogo_e_apaga_a_pasta_temporaria()
    {
        File.WriteAllText(Path.Combine(game, "a001.fake"), "name|Kenta");
        var before = Directory.EnumerateFileSystemEntries(game, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        var leftovers = () => Directory.EnumerateDirectories(Path.GetTempPath(), "vnforge-nomes-*").Count();
        var temporaryBefore = leftovers();

        ProjectInitializer.ReadGameCharacterNames(Inspect(new FakeEngine()), game, TestContext.Current.CancellationToken);

        Assert.Equal(before, Directory.EnumerateFileSystemEntries(game, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(temporaryBefore, leftovers());
    }

    [Fact]
    public void Engine_que_nao_extrai_texto_devolve_nulo()
    {
        Assert.Null(ProjectInitializer.ReadGameCharacterNames(Inspect(new NoUnpackerEngine()), game, TestContext.Current.CancellationToken));
    }

    private sealed class NoUnpackerEngine : IEngine
    {
        public string Id => "nada";

        public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];
    }
}
