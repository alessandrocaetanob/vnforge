using System.Text;
using VNForge.AI.Cost;
using VNForge.Core.Bible;
using VNForge.Core.Text;

namespace VNForge.AI.Bible;

/// <summary>Uma cena (um script do jogo) com as falas que a IA lê.</summary>
/// <param name="Key">A chave do script (<c>yst00013</c>).</param>
/// <param name="Label">O nome que o jogo ou a pessoa deu ao script, só para a IA se situar; vazio quando não há.</param>
public sealed record BibleSceneInput(string Key, string Label, IReadOnlyList<TextUnit> Lines);

/// <summary>Um personagem do jogo: o nome no original e na tradução, para a IA escrever os nomes como a tradução vai escrevê-los.</summary>
public sealed record BibleCharacterName(string Source, string Target);

/// <summary>
/// Um capítulo (ou um pedaço dele, quando não cabe no contexto) com todas as falas das cenas. O <see cref="Text"/> é o bloco que fica no cache de
/// prompt: tem que sair idêntico a cada chamada sobre o mesmo capítulo.
/// </summary>
/// <param name="Number">O número do capítulo na ordem da história; nulo quando o capítulo não é numerado.</param>
public sealed record BibleChapterInput(string Name, int? Number, IReadOnlyList<BibleSceneInput> Scenes, IReadOnlyList<BibleCharacterName> Characters)
{
    public int LineCount => Scenes.Sum(s => s.Lines.Count);

    /// <summary>O roteiro do capítulo: uma seção por cena, uma fala por linha, com o falante quando há.</summary>
    public string Text()
    {
        var sb = new StringBuilder();
        foreach (var scene in Scenes)
        {
            sb.Append("=== Cena ").Append(scene.Key);
            if (scene.Label.Length > 0)
            {
                sb.Append(" (").Append(scene.Label).Append(')');
            }

            sb.Append(" ===\n");
            foreach (var line in scene.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Source)))
            {
                sb.Append(string.IsNullOrEmpty(line.Speaker) ? string.Empty : line.Speaker + ": ").Append(line.Source).Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>Tokens do <see cref="Text"/>, pela mesma heurística do <see cref="CostEstimator"/>.</summary>
    public long Tokens() => CostEstimator.Tokens(Text());
}

/// <summary>O que a IA escreveu de cada cena de um capítulo, em resumos curtos: o que a sinopse e a linha do tempo do jogo leem.</summary>
public sealed record BibleChapterDigest(string Name, IReadOnlyList<string> Summaries);

/// <summary>A sinopse e a linha do tempo do jogo.</summary>
public sealed record BibleOverview(string Synopsis, IReadOnlyList<BibleTimelineEntry> Timeline);

/// <summary>
/// Fronteira entre a bíblia do jogo e a IA. O provedor Claude e o <see cref="FakeBibleProvider"/> dos testes a implementam; nada fora de
/// <c>VNForge.AI</c> conhece o SDK.
/// </summary>
public interface IBibleProvider
{
    /// <summary>
    /// Lê o capítulo e descreve as cenas de <paramref name="sceneKeys"/>, uma <see cref="BibleScene"/> por chave, na ordem pedida. Várias chamadas sobre o mesmo
    /// <paramref name="chapter"/> leem o mesmo roteiro: o provedor Claude o põe no cache de prompt, e as leituras seguintes custam uma fração da primeira.
    /// </summary>
    Task<IReadOnlyList<BibleScene>> DescribeScenesAsync(BibleChapterInput chapter, IReadOnlyList<string> sceneKeys, CancellationToken ct);

    /// <summary>A sinopse e a linha do tempo do jogo, a partir dos resumos de cada capítulo.</summary>
    Task<BibleOverview> SummarizeGameAsync(IReadOnlyList<BibleChapterDigest> chapters, CancellationToken ct);
}
