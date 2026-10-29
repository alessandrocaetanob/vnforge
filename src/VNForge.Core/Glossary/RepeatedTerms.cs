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
/// <param name="From">O que a IA tinha escrito no lugar, na primeira dessas falas: se a IA escreveu versões diferentes que a pessoa levou à mesma tradução, só a da primeira fala fica guardada.</param>
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

    /// <summary>Tira dos pares o que o glossário já cobre, pela mesma regra de <see cref="Excluding(IEnumerable{TermCandidate}, IEnumerable{string})"/>.</summary>
    public static IReadOnlyList<TermPair> Excluding(IEnumerable<TermPair> pairs, IEnumerable<string> known)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(known);

        var terms = known.Select(k => k.Trim()).Where(k => k.Length > 0).ToArray();
        return [.. pairs.Where(p => !terms.Any(t => t.Contains(p.Source, StringComparison.Ordinal)))];
    }

    /// <summary>Conta, tira o que já está em <paramref name="known"/> (termos do glossário e nomes de personagens) e guarda os <paramref name="maxResults"/> primeiros.</summary>
    public static IReadOnlyList<TermCandidate> Find(IEnumerable<string> sources, IEnumerable<string> known, int minFrequency = MinFrequency, int maxResults = DefaultMaxResults) =>
        [.. Excluding(Count(sources, minFrequency), known).Take(maxResults)];

    /// <summary>
    /// Os pares trecho japonês e tradução que a pessoa repetiu nas falas que corrigiu (a tradução gravada difere da <see cref="TextUnit.AiTranslation"/>), os de mais falas primeiro,
    /// já sem o que seja ambíguo. Equivale a <see cref="Offer"/> sobre <see cref="Concentrated"/> sem nada conhecido.
    /// </summary>
    /// <param name="threshold">Quantas falas bastam; no mínimo 2.</param>
    public static IReadOnlyList<TermPair> Corrected(IEnumerable<TextUnit> units, int threshold = CorrectionLearner.DefaultThreshold) =>
        Offer(Concentrated(units, threshold), []);

    /// <summary>
    /// Todos os pares trecho e troca que passam na concentração, antes de escolher o trecho de cada troca. A troca de palavras vem de <see cref="CorrectionLearner.Swaps"/>; o par vale
    /// quando a troca está em ao menos <paramref name="threshold"/> falas, em dois terços das falas corrigidas que têm o trecho (com troca ou sem, como a que só mudou a pontuação)
    /// e em dois terços das falas com essa troca (senão a troca é de estilo, não do termo). A escolha do trecho fica para <see cref="Offer"/>, que antes tira o que já está coberto.
    /// </summary>
    /// <param name="threshold">Quantas falas bastam; no mínimo 2.</param>
    public static IReadOnlyList<TermPair> Concentrated(IEnumerable<TextUnit> units, int threshold = CorrectionLearner.DefaultThreshold)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 2);

        var tally = new PairTally();
        foreach (var unit in units)
        {
            if (unit.AiTranslation is not null && unit.HasTranslation && !string.Equals(unit.AiTranslation, unit.Translation, StringComparison.Ordinal))
            {
                tally.Add(unit);
            }
        }

        return tally.Concentrated(threshold);
    }

    /// <summary>
    /// Tira de <paramref name="concentrated"/> o que <paramref name="known"/> cobre (pela regra de <see cref="Excluding(IEnumerable{TermPair}, IEnumerable{string})"/>) e só então escolhe, em cada troca,
    /// o trecho com mais falas. Fica de fora a troca em que dois trechos empatam e o trecho que vence em mais de uma troca (não dá para saber qual troca é dele).
    /// Dois termos novos que dividem exatamente as mesmas falas também se empatam, então nenhum dos dois sai. Os de mais falas primeiro; empate pelo trecho (ordinal).
    /// </summary>
    public static IReadOnlyList<TermPair> Offer(IEnumerable<TermPair> concentrated, IEnumerable<string> known)
    {
        ArgumentNullException.ThrowIfNull(concentrated);
        ArgumentNullException.ThrowIfNull(known);

        var winners = new List<TermPair>();
        foreach (var bySwap in Excluding(concentrated, known).GroupBy(p => p.Target.ToLowerInvariant()))
        {
            var best = bySwap.Max(p => p.Lines);
            var top = bySwap.Where(p => p.Lines == best).ToList();
            if (top.Count == 1)
            {
                winners.Add(top[0]);
            }
        }

        return
        [
            .. winners
                .GroupBy(p => p.Source, StringComparer.Ordinal)
                .Where(g => g.Count() == 1)
                .Select(g => g.Single())
                .OrderByDescending(p => p.Lines)
                .ThenBy(p => p.Source, StringComparer.Ordinal),
        ];
    }

    private sealed class PairTally
    {
        private readonly Dictionary<string, int> runLines = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> swapLines = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Run, string Key), Pair> pairs = [];

        public void Add(TextUnit unit)
        {
            var byKey = new Dictionary<string, WordSwap>(StringComparer.Ordinal);
            foreach (var swap in CorrectionLearner.Swaps(unit.AiTranslation, unit.Translation))
            {
                byKey.TryAdd(swap.To.ToLowerInvariant(), swap);
            }

            // A troca conta mesmo numa fala sem trecho japonês elegível: é o que mostra que ela é de estilo e não de um termo.
            foreach (var key in byKey.Keys)
            {
                swapLines[key] = swapLines.GetValueOrDefault(key) + 1;
            }

            // O trecho conta em toda fala corrigida que o tem, mesmo sem troca (só a pontuação mudou): é o que mostra que a troca não é dele.
            var runs = new HashSet<string>(StringComparer.Ordinal);
            CollectRuns(unit.Source, runs);
            foreach (var run in runs)
            {
                runLines[run] = runLines.GetValueOrDefault(run) + 1;
                foreach (var (key, swap) in byKey)
                {
                    if (!pairs.TryGetValue((run, key), out var pair))
                    {
                        pairs[(run, key)] = pair = new Pair(swap);
                    }

                    pair.Lines++;
                }
            }
        }

        public IReadOnlyList<TermPair> Concentrated(int threshold) =>
        [
            .. pairs
                .Where(p => IsConcentrated(p.Key, p.Value, threshold))
                .Select(p => new TermPair(p.Key.Run, p.Value.Swap.To, p.Value.Swap.From, p.Value.Lines)),
        ];

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
