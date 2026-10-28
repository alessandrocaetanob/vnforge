using VNForge.Core.Engines;
using VNForge.Core.Text;

namespace VNForge.Core.Scenes;

/// <summary>Como uma cena leva a outra.</summary>
public enum SceneEdgeKind
{
    /// <summary>Salto sem volta (<c>GO</c> no YU-RIS): a história segue na outra cena.</summary>
    Jump,

    /// <summary>Chamada com volta (<c>GOSUB</c> no YU-RIS): a outra cena roda e devolve o controle.</summary>
    Call,

    /// <summary>O destino de uma escolha do jogador: a cena que a escolha leva a seguir.</summary>
    Choice,
}

/// <summary>
/// Uma aresta do grafo de cenas: a cena <see cref="From"/> leva à cena <see cref="To"/>. As cenas são identificadas pela
/// <see cref="ScriptFile.Key"/> do script, a mesma que abre os <see cref="TextUnit.Id"/>.
/// </summary>
/// <param name="From">Chave do script de origem.</param>
/// <param name="To">Chave do script de destino.</param>
/// <param name="Kind">Como a origem leva ao destino.</param>
/// <param name="Label">Nome do rótulo de destino no jogo (<c>NEXT_STORY01</c>), para mostrar e depurar.</param>
public sealed record SceneEdge(string From, string To, SceneEdgeKind Kind, string Label)
{
    /// <summary>
    /// Só nas arestas <see cref="SceneEdgeKind.Choice"/>: o <see cref="TextUnit.Id"/> do texto da escolha que leva a este destino, quando
    /// a extração tem esse texto. Nulo se a engine não sabe qual texto corresponde (o destino existe, mas a unidade não).
    /// </summary>
    public string? ChoiceUnitId { get; init; }
}
