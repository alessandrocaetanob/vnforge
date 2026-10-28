using VNForge.Core.Engines;
using VNForge.Core.Scenes;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Scenes;

public class SceneGraphTests
{
    private static SceneEdge Jump(string from, string to, string label = "L") => new(from, to, SceneEdgeKind.Jump, label);

    private static SceneEdge Call(string from, string to, string label = "L") => new(from, to, SceneEdgeKind.Call, label);

    private static SceneEdge Choice(string from, string to, string label = "L", string? unitId = null) =>
        new(from, to, SceneEdgeKind.Choice, label) { ChoiceUnitId = unitId };

    [Fact]
    public void Read_EngineSemSuporte_DevolveGrafoVazio()
    {
        var engine = new FakeEngine();

        var graph = SceneGraph.Read(engine, new UnpackedGame(new TestContext(), []));

        Assert.Empty(graph.Edges);
        Assert.Empty(graph.Scenes);
    }

    [Fact]
    public void Read_UsaAsArestasDaEngine()
    {
        var engine = new EdgeEngine([Jump("a", "b"), Jump("b", "c")]);

        var graph = SceneGraph.Read(engine, new UnpackedGame(new TestContext(), []));

        Assert.Equal(2, graph.Edges.Count);
        Assert.Equal(["a", "b", "c"], graph.Scenes);
    }

    [Fact]
    public void Construtor_DescartaSaltoECallParaOMesmoScript_MasGuardaEscolha()
    {
        var graph = new SceneGraph([Jump("a", "a"), Call("a", "a"), Choice("a", "a", "NEXT_01", "yst00001:000256")]);

        var edge = Assert.Single(graph.Edges);
        Assert.Equal(SceneEdgeKind.Choice, edge.Kind);
        Assert.Equal("yst00001:000256", edge.ChoiceUnitId);
    }

    [Fact]
    public void Construtor_JuntaArestasIguais_EOrdenaDeFormaEstavel()
    {
        var graph = new SceneGraph([Jump("b", "c"), Jump("a", "c"), Jump("a", "b"), Jump("a", "b")]);

        Assert.Equal(
            [("a", "b"), ("a", "c"), ("b", "c")],
            graph.Edges.Select(e => (e.From, e.To)));
    }

    [Fact]
    public void Construtor_MesmoDestinoPorTiposDiferentes_GuardaAsDuas()
    {
        var graph = new SceneGraph([Jump("a", "b"), Call("a", "b")]);

        Assert.Equal(2, graph.Edges.Count);
    }

    [Fact]
    public void From_To_ListamAsArestasDeCadaCena()
    {
        var graph = new SceneGraph([Jump("a", "b"), Jump("a", "c"), Jump("b", "c")]);

        Assert.Equal(["b", "c"], graph.From("a").Select(e => e.To));
        Assert.Equal(["a", "b"], graph.To("c").Select(e => e.From));
        Assert.Empty(graph.From("c"));
        Assert.Empty(graph.To("a"));
        Assert.Empty(graph.From("inexistente"));
    }

    [Fact]
    public void From_IgnoraACaixaDaChave()
    {
        var graph = new SceneGraph([Jump("yst00001", "yst00002")]);

        Assert.Single(graph.From("YST00001"));
    }

    [Fact]
    public void ReachableFrom_SegueSaltosEscolhasECalls_SemRepetir()
    {
        var graph = new SceneGraph([Jump("a", "b"), Choice("b", "c"), Call("c", "d"), Jump("d", "b"), Jump("x", "y")]);

        Assert.Equal(["b", "c", "d"], graph.ReachableFrom("a").Order());
    }

    [Fact]
    public void ReachableFrom_SemSaidas_DevolveVazio()
    {
        var graph = new SceneGraph([Jump("a", "b")]);

        Assert.Empty(graph.ReachableFrom("b"));
    }

    [Fact]
    public void Restrict_MantemSoArestasEntreAsCenasPedidas()
    {
        var graph = new SceneGraph([Jump("a", "b"), Call("a", "eris"), Jump("b", "c")]);

        var restricted = graph.Restrict(new HashSet<string> { "a", "b", "c" });

        Assert.Equal([("a", "b"), ("b", "c")], restricted.Edges.Select(e => (e.From, e.To)));
        Assert.Equal(3, graph.Edges.Count);
    }

    [Fact]
    public void Empty_NaoTemArestas()
    {
        Assert.Empty(SceneGraph.Empty.Edges);
    }

    [Fact]
    public void Argumentos_Nulos_Recusam()
    {
        Assert.Throws<ArgumentNullException>(() => new SceneGraph(null!));
        Assert.Throws<ArgumentNullException>(() => SceneGraph.Read(null!, new UnpackedGame(new TestContext(), [])));
        Assert.Throws<ArgumentNullException>(() => SceneGraph.Read(new FakeEngine(), null!));
    }

    private sealed class TestContext() : EngineContext("fake");

    private sealed class EdgeEngine(IReadOnlyList<SceneEdge> edges) : IEngine
    {
        public string Id => "edge";

        public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];

        public IReadOnlyList<SceneEdge> ReadSceneEdges(UnpackedGame game) => edges;
    }
}
