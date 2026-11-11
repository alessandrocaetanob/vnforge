using System.Diagnostics.CodeAnalysis;
using VNForge.Core.Text;

namespace VNForge.Core.Eval;

/// <summary>
/// O conjunto ouro do banco de provas: as linhas que a pessoa aprovou. Cada configuração traduz o original dessas linhas e o resultado é comparado com a
/// tradução aprovada. O sorteio é determinístico para uma mesma semente, para a rodada poder ser repetida com a mesma amostra.
/// </summary>
public static class GoldSet
{
    private const int NeighborMaxLength = 70;

    /// <summary>
    /// O maior trecho sorteado. Trechos curtos espalham a amostra por mais scripts, e cada trecho ainda tem vizinhas para servir de contexto;
    /// trechos de um lote inteiro (30 linhas) deixariam 200 linhas em uns sete lugares do jogo.
    /// </summary>
    public const int MaxBlockLines = 10;

    /// <summary>As linhas aprovadas com original e tradução, na ordem dos scripts, cada uma com a fala de antes e a de depois.</summary>
    public static IReadOnlyList<GoldLine> From(IEnumerable<KeyValuePair<string, IReadOnlyList<TextUnit>>> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var gold = new List<GoldLine>();
        foreach (var (key, units) in texts)
        {
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit.Status != TranslationStatus.Approved || string.IsNullOrWhiteSpace(unit.Source) || string.IsNullOrWhiteSpace(unit.Translation))
                {
                    continue;
                }

                gold.Add(new GoldLine(unit.Id, key, unit.Speaker, unit.Source, unit.Translation, Neighbor(units, i - 1), Neighbor(units, i + 1), i));
            }
        }

        return gold;
    }

    /// <summary>Quantas linhas ainda não têm tradução: é para elas que o custo e o tempo do jogo inteiro valem.</summary>
    public static int RemainingLines(IEnumerable<KeyValuePair<string, IReadOnlyList<TextUnit>>> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return texts.Sum(script => script.Value.Count(u => u.Status == TranslationStatus.Untranslated && !string.IsNullOrWhiteSpace(u.Source)));
    }

    /// <summary>Sorteia <paramref name="count"/> linhas (todas, se o conjunto for menor) e as devolve na ordem original.</summary>
    [SuppressMessage("Security Hotspot", "S2245:Using pseudorandom number generators (PRNGs) is security-sensitive", Justification = "O sorteio precisa se repetir com a mesma semente para a rodada ser reproduzida; ele não protege nada.")]
    public static IReadOnlyList<GoldLine> Sample(IReadOnlyList<GoldLine> gold, int count, int seed)
    {
        ArgumentNullException.ThrowIfNull(gold);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count >= gold.Count)
        {
            return gold;
        }

        var random = new Random(seed);
        var indexes = Enumerable.Range(0, gold.Count).ToArray();
        for (var i = 0; i < count; i++)
        {
            var j = random.Next(i, indexes.Length);
            (indexes[i], indexes[j]) = (indexes[j], indexes[i]);
        }

        return [.. indexes.Take(count).Order().Select(i => gold[i])];
    }

    /// <summary>
    /// Sorteia trechos em vez de linhas soltas: cada trecho tem até <paramref name="blockSize"/> linhas aprovadas coladas uma na outra no mesmo script (uma linha não aprovada no meio abre outro trecho), e cada trecho vira um lote com o
    /// contexto que o <c>vnf translate</c> daria a ele (as linhas do lote entre si e as falas de antes e de depois). Os trechos saem na ordem original, com as linhas em ordem.
    /// O sorteio pesa cada trecho pelo tamanho (sem repor): um trecho de 10 linhas tem dez vezes a chance de um de 1. Sem o peso, as aprovações espalhadas, que dão muitos trechos de uma linha,
    /// encheriam a amostra de chamadas com o prompt de sistema inteiro e quase nenhum contexto.
    /// Sorteia trechos inteiros até chegar a <paramref name="count"/> linhas; o último é cortado para o total bater. Com <paramref name="count"/> maior que o conjunto, devolve tudo.
    /// </summary>
    [SuppressMessage("Security Hotspot", "S2245:Using pseudorandom number generators (PRNGs) is security-sensitive", Justification = "O sorteio precisa se repetir com a mesma semente para a rodada ser reproduzida; ele não protege nada.")]
    public static IReadOnlyList<IReadOnlyList<GoldLine>> SampleBlocks(IReadOnlyList<GoldLine> gold, int count, int seed, int blockSize)
    {
        ArgumentNullException.ThrowIfNull(gold);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 1);
        var blocks = new List<List<GoldLine>>();
        foreach (var line in gold)
        {
            if (blocks.Count == 0 || blocks[^1].Count >= blockSize || blocks[^1][0].ScriptKey != line.ScriptKey || IsGap(blocks[^1][^1], line))
            {
                blocks.Add([]);
            }

            blocks[^1].Add(line);
        }

        if (count >= gold.Count)
        {
            return blocks;
        }

        // Amostragem ponderada sem reposição (Efraimidis e Spirakis): a chave u^(1/peso) dá a cada trecho a chance proporcional ao tamanho.
        var random = new Random(seed);
        var order = Enumerable.Range(0, blocks.Count)
            .Select(i => (Index: i, Key: Math.Log(1.0 - random.NextDouble()) / blocks[i].Count))
            .OrderByDescending(k => k.Key)
            .ToList();
        var chosen = new List<(int Index, List<GoldLine> Lines)>();
        var taken = 0;
        foreach (var (index, _) in order)
        {
            if (taken >= count)
            {
                break;
            }

            var block = blocks[index];
            var take = Math.Min(block.Count, count - taken);
            chosen.Add((index, take == block.Count ? block : block.GetRange(0, take)));
            taken += take;
        }

        return [.. chosen.OrderBy(c => c.Index).Select(c => (IReadOnlyList<GoldLine>)c.Lines)];
    }

    private static bool IsGap(GoldLine previous, GoldLine next) =>
        previous.Position >= 0 && next.Position >= 0 && next.Position != previous.Position + 1;

    private static string Neighbor(IReadOnlyList<TextUnit> units, int index)
    {
        if (index < 0 || index >= units.Count)
        {
            return string.Empty;
        }

        var unit = units[index];
        var text = string.IsNullOrWhiteSpace(unit.Translation) ? unit.Source : unit.Translation;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = text.ReplaceLineEndings(" ").Trim();
        if (text.Length > NeighborMaxLength)
        {
            text = text[..NeighborMaxLength].TrimEnd() + "...";
        }

        return string.IsNullOrWhiteSpace(unit.Speaker) ? $"\"{text}\"" : $"{unit.Speaker}, \"{text}\"";
    }
}
