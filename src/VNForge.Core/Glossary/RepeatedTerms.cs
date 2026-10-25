namespace VNForge.Core.Glossary;

/// <summary>Uma sequência de kanji e katakana que se repete no original.</summary>
/// <param name="Source">A sequência, como aparece no texto do jogo.</param>
/// <param name="Lines">Em quantas linhas do original ela aparece (linhas, não ocorrências dentro da linha).</param>
public sealed record TermCandidate(string Source, int Lines);

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
