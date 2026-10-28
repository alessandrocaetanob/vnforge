using VNForge.Core.Engines;

namespace VNForge.Core.Scenes;

/// <summary>
/// O grafo de cenas de um jogo: quais scripts levam a quais, por salto, chamada ou escolha. Imutável. Quem monta é a engine
/// (<see cref="IEngine.ReadSceneEdges"/>); o grafo só normaliza as arestas: tira repetidas, descarta salto e chamada para o
/// próprio script (rótulos internos, que não são ligação entre cenas) e ordena de forma estável. A aresta de escolha para o próprio
/// script fica, porque diz que a cena tem uma escolha mesmo quando os ramos voltam a se encontrar nela. Arestas iguais em todos os
/// campos se juntam; duas escolhas com o mesmo rótulo e destino que não têm <see cref="SceneEdge.ChoiceUnitId"/> viram uma só.
/// </summary>
public sealed class SceneGraph
{
    private readonly ILookup<string, SceneEdge> _from;
    private readonly ILookup<string, SceneEdge> _to;

    /// <summary>Cria o grafo a partir de arestas soltas.</summary>
    /// <param name="edges">Arestas, em qualquer ordem; repetidas e saltos para o próprio script são descartados.</param>
    public SceneGraph(IEnumerable<SceneEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        Edges = edges
            .Where(e => e.Kind == SceneEdgeKind.Choice || !Same(e.From, e.To))
            .Distinct()
            .OrderBy(e => e.From, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.To, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Kind)
            .ThenBy(e => e.Label, StringComparer.Ordinal)
            .ThenBy(e => e.ChoiceUnitId, StringComparer.Ordinal)
            .ToList();
        _from = Edges.ToLookup(e => e.From, StringComparer.OrdinalIgnoreCase);
        _to = Edges.ToLookup(e => e.To, StringComparer.OrdinalIgnoreCase);
        Scenes = Edges
            .SelectMany(e => new[] { e.From, e.To })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Grafo sem arestas: o que uma engine sem suporte a saltos devolve.</summary>
    public static SceneGraph Empty { get; } = new([]);

    /// <summary>Arestas ordenadas por origem, destino, tipo e rótulo, sem repetição.</summary>
    public IReadOnlyList<SceneEdge> Edges { get; }

    /// <summary>Chaves dos scripts que aparecem em alguma aresta, em ordem.</summary>
    public IReadOnlyList<string> Scenes { get; }

    /// <summary>Monta o grafo do jogo com as arestas que a engine lê; engine sem suporte dá o <see cref="Empty"/>.</summary>
    /// <param name="engine">Engine do jogo.</param>
    /// <param name="game">Pasta descompactada, de <see cref="IGameUnpacker.Open"/>.</param>
    public static SceneGraph Read(IEngine engine, UnpackedGame game)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(game);
        return new SceneGraph(engine.ReadSceneEdges(game));
    }

    /// <summary>Arestas que saem da cena <paramref name="scene"/> (chave do script, sem diferenciar caixa).</summary>
    public IReadOnlyList<SceneEdge> From(string scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return [.. _from[scene]];
    }

    /// <summary>Arestas que chegam à cena <paramref name="scene"/>.</summary>
    public IReadOnlyList<SceneEdge> To(string scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return [.. _to[scene]];
    }

    /// <summary>
    /// Cenas que se alcança partindo de <paramref name="scene"/> por qualquer tipo de aresta. Não inclui a própria cena, a não ser que
    /// um caminho volte a ela.
    /// </summary>
    public IReadOnlyCollection<string> ReachableFrom(string scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        pending.Enqueue(scene);
        while (pending.TryDequeue(out var current))
        {
            foreach (var edge in _from[current])
            {
                if (seen.Add(edge.To))
                {
                    pending.Enqueue(edge.To);
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// Subgrafo com as arestas cujas duas pontas estão em <paramref name="scenes"/>. Serve para tirar do grafo os scripts que não são cenas
    /// (no YU-RIS, as rotinas do E-ris que todo script chama).
    /// </summary>
    public SceneGraph Restrict(IReadOnlySet<string> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        var keys = new HashSet<string>(scenes, StringComparer.OrdinalIgnoreCase);
        return new SceneGraph(Edges.Where(e => keys.Contains(e.From) && keys.Contains(e.To)));
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
