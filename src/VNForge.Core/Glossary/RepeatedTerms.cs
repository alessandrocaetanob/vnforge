using VNForge.Core.Review;
using VNForge.Core.Text;

namespace VNForge.Core.Glossary;

/// <summary>Uma sequência de kanji e katakana que se repete no original.</summary>
/// <param name="Source">A sequência, como aparece no texto do jogo.</param>
/// <param name="Lines">Em quantas linhas do original ela aparece (linhas, não ocorrências dentro da linha).</param>
public sealed record TermCandidate(string Source, int Lines);

/// <summary>Um trecho japonês que a pessoa traduziu do mesmo jeito em várias falas que corrigiu, pronto para virar termo do glossário.</summary>
/// <param name="Source">O trecho, como aparece no original.</param>
/// <param name="Target">A tradução que a pessoa pôs, com a grafia da primeira fala em que a pôs.</param>
/// <param name="From">O que a IA tinha escrito no lugar, na primeira dessas falas.</param>
/// <param name="Lines">Em quantas falas corrigidas o trecho ganhou essa tradução.</param>
public sealed record TermPair(string Source, string Target, string From, int Lines);

/// <summary>
/// Acha os termos que o glossário ainda não cobre: sequências de kanji e katakana (com a barra de prolongamento) de 2 a 6 caracteres que
/// aparecem em ao menos <see cref="MinFrequency"/> linhas. Só conta, sem IA: quem traduz o lote é o <c>VNForge.AI</c>. Uma sequência maior que
/// <see cref="MaxLength"/> não vira candidata nem se parte em pedaços, porque a maior parte é uma frase, não um termo.
/// </summary>
public static class RepeatedTerms
{
    public const int MinFrequency = 5;
    public const int MinLength = 2;
    public const int MaxLength = 6;
    public const int DefaultMaxResults = 60;

    /// <summary>Todas as sequências com a frequência mínima, da mais frequente para a menos; empate pelo texto (ordinal).</summary>
    public static IReadOnlyList<TermCandidate> Count(IEnumerable<string> sources, int minFrequency = MinFrequency)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var lines = new Dictionary<string, int>(StringComparer.Ordinal);
        var inLine = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            inLine.Clear();
            CollectRuns(source, inLine);
            foreach (var run in inLine)
            {
                lines[run] = lines.GetValueOrDefault(run) + 1;
            }
        }

        return
        [
            .. lines
                .Where(p => p.Value >= minFrequency)
                .Select(p => new TermCandidate(p.Key, p.Value))
                .OrderByDescending(c => c.Lines)
                .ThenBy(c => c.Source, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Tira dos candidatos o que o glossário já cobre: a sequência igual a um termo ou contida em um (a parte já está sob um termo fixo).
    /// Termo vazio ou só com espaços não exclui nada.
    /// </summary>
    public static IReadOnlyList<TermCandidate> Excluding(IEnumerable<TermCandidate> candidates, IEnumerable<string> known)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(known);

        var terms = known.Select(k => k.Trim()).Where(k => k.Length > 0).ToArray();
        return [.. candidates.Where(c => !terms.Any(t => t.Contains(c.Source, StringComparison.Ordinal)))];
    }

    /// <summary>Conta, tira o que já está em <paramref name="known"/> (termos do glossário e nomes de personagens) e guarda os <paramref name="maxResults"/> primeiros.</summary>
    public static IReadOnlyList<TermCandidate> Find(IEnumerable<string> sources, IEnumerable<string> known, int minFrequency = MinFrequency, int maxResults = DefaultMaxResults) =>
        [.. Excluding(Count(sources, minFrequency), known).Take(maxResults)];

    /// <summary>Tira dos pares o que o glossário já cobre, pela mesma regra de <see cref="Excluding(IEnumerable{TermCandidate}, IEnumerable{string})"/>.</summary>
    public static IReadOnlyList<TermPair> Excluding(IEnumerable<TermPair> pairs, IEnumerable<string> known)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(known);

        var terms = known.Select(k => k.Trim()).Where(k => k.Length > 0).ToArray();
        return [.. pairs.Where(p => !terms.Any(t => t.Contains(p.Source, StringComparison.Ordinal)))];
    }

    /// <summary>
    /// Os pares trecho japonês e tradução que a pessoa repetiu nas falas que corrigiu (a tradução gravada difere da <see cref="TextUnit.AiTranslation"/>), os de mais falas primeiro.
    /// A troca de palavras vem de <see cref="CorrectionLearner.Swaps"/>; o par vale quando a troca está em ao menos <paramref name="threshold"/> falas, em dois terços das falas corrigidas
    /// que têm o trecho e em dois terços das falas com essa troca (senão a troca é de estilo, não do termo). Dois trechos que disputam a mesma troca no mesmo número de falas ficam de fora.
    /// </summary>
    /// <param name="threshold">Quantas falas bastam; no mínimo 2.</param>
    public static IReadOnlyList<TermPair> Corrected(IEnumerable<TextUnit> units, int threshold = CorrectionLearner.DefaultThreshold)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 2);

        var tally = new PairTally();
        foreach (var unit in units)
        {
            if (unit.AiTranslation is not null && unit.HasTranslation)
            {
                tally.Add(unit);
            }
        }

        return tally.Offer(threshold);
    }

    private sealed class PairTally
    {
        private readonly Dictionary<string, int> runLines = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> swapLines = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Run, string Key), Pair> pairs = [];

        public void Add(TextUnit unit)
        {
            var swaps = CorrectionLearner.Swaps(unit.AiTranslation, unit.Translation);
            var runs = new HashSet<string>(StringComparer.Ordinal);
            if (swaps.Count > 0)
            {
                CollectRuns(unit.Source, runs);
            }

            if (runs.Count == 0)
            {
                return;
            }

            var byKey = new Dictionary<string, WordSwap>(StringComparer.Ordinal);
            foreach (var swap in swaps)
            {
                byKey.TryAdd(swap.To.ToLowerInvariant(), swap);
            }

            foreach (var run in runs)
            {
                runLines[run] = runLines.GetValueOrDefault(run) + 1;
            }

            foreach (var (key, swap) in byKey)
            {
                swapLines[key] = swapLines.GetValueOrDefault(key) + 1;
                foreach (var run in runs)
                {
                    if (!pairs.TryGetValue((run, key), out var pair))
                    {
                        pairs[(run, key)] = pair = new Pair(swap);
                    }

                    pair.Lines++;
                }
            }
        }

        public IReadOnlyList<TermPair> Offer(int threshold)
        {
            var offered = new List<TermPair>();
            foreach (var byKey in pairs.Where(p => IsConcentrated(p.Key, p.Value, threshold)).GroupBy(p => p.Key.Key))
            {
                var best = byKey.Max(p => p.Value.Lines);
                var top = byKey.Where(p => p.Value.Lines == best).ToList();
                if (top.Count == 1)
                {
                    var (run, _) = top[0].Key;
                    offered.Add(new TermPair(run, top[0].Value.Swap.To, top[0].Value.Swap.From, best));
                }
            }

            return [.. offered.OrderByDescending(p => p.Lines).ThenBy(p => p.Source, StringComparer.Ordinal)];
        }

        private bool IsConcentrated((string Run, string Key) key, Pair pair, int threshold) =>
            pair.Lines >= threshold && pair.Lines * 3 >= runLines[key.Run] * 2 && pair.Lines * 3 >= swapLines[key.Key] * 2;
    }

    private sealed class Pair(WordSwap swap)
    {
        public WordSwap Swap { get; } = swap;

        public int Lines { get; set; }
    }

    private static void CollectRuns(string text, HashSet<string> into)
    {
        var start = -1;
        var real = false;
        for (var i = 0; i <= text.Length; i++)
        {
            var inRun = i < text.Length && IsTermChar(text[i]);
            if (inRun && start < 0)
            {
                start = i;
                real = false;
            }
            else if (!inRun && start >= 0)
            {
                var length = i - start;
                if (real && length is >= MinLength and <= MaxLength)
                {
                    into.Add(text.Substring(start, length));
                }

                start = -1;
            }

            real |= inRun && IsRealChar(text[i]);
        }
    }

    // "ーー", "ッッ" e "ァァ" não são termo: a sequência precisa de ao menos um kanji ou um katakana de verdade (não a barra de prolongamento, nem os pequenos).
    private static bool IsRealChar(char c) =>
        c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿'
            || (c is >= 'ァ' and <= 'ヺ' && !"ァィゥェォッャュョヮヵヶ".Contains(c, StringComparison.Ordinal));

    private static bool IsTermChar(char c) =>
        c is >= '一' and <= '鿿'
            or >= '㐀' and <= '䶿'
            or '々'
            or >= 'ァ' and <= 'ヺ'
            or 'ー';
}
