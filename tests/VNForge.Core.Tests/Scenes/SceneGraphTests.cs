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
    public void ReachableFrom_SegueSaltosEscolhasECalls_SemRepetir()
    {
        var graph = new SceneGraph([Jump("a", "b"), Choice("b", "c"), Call("c", "d"), Jump("d", "b"), Jump("x", "y")]);

        Assert.Equal(["b", "c", "d"], graph.ReachableFrom("a").Order());
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

    private sealed class TestContext() : EngineContext("fake");
}
