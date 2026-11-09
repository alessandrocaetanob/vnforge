namespace VNForge.Core.Jmdict;

/// <summary>
/// A consulta do dicionário offline (#538): dado um texto japonês sem espaços e a posição do mouse, acha a palavra que cobre o caractere. Para cada começo possível testa os trechos do
/// mais longo para o mais curto; a forma de dicionário vale antes de qualquer conjugação, e a conjugação só entra quando o trecho não é uma entrada. Fica com o trecho mais longo
/// que cobre a posição. Só usa o índice local: sem rede.
/// </summary>
public sealed class JmdictLookup(JmdictIndex index)
{
    /// <summary>Os caracteres que o trecho de uma palavra ganha além da maior forma do dicionário, para caber uma conjugação comprida (<c>食べさせられなかった</c>).</summary>
    private const int InflectionMargin = 8;

    private readonly int maxSpan = Math.Clamp(index.MaxKeyLength + InflectionMargin, 4, 24);

    /// <summary>A palavra que cobre <paramref name="position"/> em <paramref name="text"/>; nulo sobre pontuação, marcadores e o que o dicionário não tem.</summary>
    public JmdictMatch? FindWordAt(string text, int position)
    {
        if (position < 0 || position >= text.Length || IsBoundary(text[position]))
        {
            return null;
        }

        var (from, to) = SegmentAround(text, position);
        JmdictMatch? best = null;
        for (var start = Math.Max(from, position - maxSpan + 1); start <= position; start++)
        {
            // Só interessa o trecho que cobre a posição e é mais longo que o melhor até aqui; no empate fica o que começa mais à esquerda.
            for (var length = Math.Min(maxSpan, to - start); length > position - start && length > (best?.Length ?? 0); length--)
            {
                if (Find(text.Substring(start, length)) is { } match)
                {
                    best = match with { Start = start, Length = length };
                    break;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// As palavras do texto, da esquerda para a direita, sem se sobrepor: em cada posição fica a palavra mais longa que começa ali. Pontuação e trechos que o dicionário não tem
    /// são pulados. É o que a linha de comando mostra para uma frase inteira.
    /// </summary>
    public IReadOnlyList<JmdictMatch> Scan(string text)
    {
        var words = new List<JmdictMatch>();
        var position = 0;
        while (position < text.Length)
        {
            var found = IsBoundary(text[position]) ? null : LongestFrom(text, position);
            if (found is null)
            {
                position++;
                continue;
            }

            words.Add(found);
            position += found.Length;
        }

        return words;
    }

    private JmdictMatch? LongestFrom(string text, int start)
    {
        var end = start + 1;
        while (end < text.Length && !IsBoundary(text[end]))
        {
            end++;
        }

        for (var length = Math.Min(maxSpan, end - start); length > 0; length--)
        {
            if (Find(text.Substring(start, length)) is { } match)
            {
                return match with { Start = start, Length = length };
            }
        }

        return null;
    }

    /// <summary>A palavra que é exatamente <paramref name="word"/>, conjugada ou não; nulo se o texto tem mais de uma palavra ou nenhuma.</summary>
    public JmdictMatch? Find(string word)
    {
        word = word.Trim();
        if (word.Length == 0)
        {
            return null;
        }

        var exact = index.Find(word);
        if (exact.Count > 0)
        {
            return new JmdictMatch(0, word.Length, word, exact, []);
        }

        return FindInflected(word);
    }

    private JmdictMatch? FindInflected(string word)
    {
        var found = new List<(JmdictEntry Entry, Deinflected How)>();
        foreach (var candidate in Deinflector.Candidates(word))
        {
            foreach (var entry in index.Find(candidate.Text))
            {
                var entryClass = Deinflector.ClassOf(entry.Senses.SelectMany(s => s.PartsOfSpeech));
                if ((entryClass & candidate.Class) != 0 && found.All(f => f.Entry.Id != entry.Id))
                {
                    found.Add((entry, candidate));
                }
            }
        }

        if (found.Count == 0)
        {
            return null;
        }

        // A palavra e as razões mostradas são as da entrada mais comum; as outras seguem na lista.
        var ordered = found.OrderBy(f => f.Entry.Rank).ThenBy(f => f.Entry.Id).ToList();
        var main = ordered[0].How;
        return new JmdictMatch(0, word.Length, main.Text, [.. ordered.Select(f => f.Entry)], [.. main.Reasons.Reverse()]);
    }

    private static bool IsBoundary(char c) => char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsControl(c) || c == '￼';

    private static (int From, int To) SegmentAround(string text, int position)
    {
        var from = position;
        while (from > 0 && !IsBoundary(text[from - 1]))
        {
            from--;
        }

        var to = position + 1;
        while (to < text.Length && !IsBoundary(text[to]))
        {
            to++;
        }

        return (from, to);
    }
}
