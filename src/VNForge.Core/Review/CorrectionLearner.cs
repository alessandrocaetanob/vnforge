using VNForge.Core.Text;

namespace VNForge.Core.Review;

/// <summary>Onde uma troca repetida vale: nas falas de um personagem (o "Como fala" dele) ou no projeto inteiro (o <c>style.md</c>).</summary>
public enum CorrectionScope
{
    Character,
    Project,
}

/// <summary>Uma troca de palavras entre a tradução da IA e a que a pessoa gravou: <paramref name="From"/> saiu e <paramref name="To"/> entrou no lugar.</summary>
public sealed record WordSwap(string From, string To);

/// <summary>
/// Uma troca que a pessoa fez em várias linhas, com a regra pronta para o guia de estilo ou para o "Como fala" do personagem.
/// </summary>
/// <param name="Scope">Onde a regra vale.</param>
/// <param name="Speaker">O falante da linha, como no jogo, quando <paramref name="Scope"/> é <see cref="CorrectionScope.Character"/>; nulo no projeto.</param>
/// <param name="Swap">A troca, como apareceu na primeira linha em que a pessoa a fez.</param>
/// <param name="Lines">As linhas em que a troca aconteceu, no escopo da regra (script e id).</param>
public sealed record RepeatedCorrection(CorrectionScope Scope, string? Speaker, WordSwap Swap, IReadOnlyList<(string ScriptKey, string UnitId)> Lines)
{
    /// <summary>Quantas linhas tiveram a troca.</summary>
    public int Count => Lines.Count;

    /// <summary>A regra como entra no <c>style.md</c>.</summary>
    public string StyleLine => $"Use \"{Swap.To}\" no lugar de \"{Swap.From}\".";

    /// <summary>A regra como entra no "Como fala" do personagem, depois do que já está escrito lá.</summary>
    public string ToneNote => $"diz \"{Swap.To}\" no lugar de \"{Swap.From}\"";

    /// <summary>A frase da regra no lugar onde ela vai: <see cref="StyleLine"/> no projeto, <see cref="ToneNote"/> no personagem.</summary>
    public string Phrase => Scope == CorrectionScope.Project ? StyleLine : ToneNote;

    /// <summary>A regra já está escrita em <paramref name="text"/> (o <c>style.md</c> ou o "Como fala"), sem diferenciar maiúsculas.</summary>
    public bool IsIn(string? text) => !string.IsNullOrEmpty(text) && text.Contains(Phrase, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// O texto de <paramref name="existing"/> com a regra no fim: uma linha de lista no <c>style.md</c> ou, no "Como fala", a frase depois de um ponto e vírgula.
    /// Se a regra já está lá, devolve o texto como está.
    /// </summary>
    public string AddTo(string? existing)
    {
        existing ??= string.Empty;
        if (IsIn(existing))
        {
            return existing;
        }

        var trimmed = existing.TrimEnd();
        if (Scope == CorrectionScope.Project)
        {
            return trimmed.Length == 0 ? $"- {StyleLine}\n" : $"{trimmed}\n- {StyleLine}\n";
        }

        var head = trimmed.TrimEnd(';', ',', '.', ' ');
        return head.Length == 0 ? ToneNote : $"{head}; {ToneNote}";
    }
}

/// <summary>
/// Acha, nas linhas que a pessoa corrigiu, as trocas de palavras que se repetem (Ei por Ô nas falas do Kenta). Compara a tradução que a IA escreveu
/// (<see cref="TextUnit.AiTranslation"/>) com a gravada (<see cref="TextUnit.Translation"/>), palavra a palavra, sem chamar a IA.
/// </summary>
/// <remarks>
/// Só conta troca de palavras (de uma a <see cref="MaxWords"/> de cada lado). Pontuação, maiúsculas e minúsculas e acréscimos ou cortes puros ficam de fora. Uma linha reescrita
/// (mais de <see cref="MaxSwapsPerLine"/> trocas) não ensina regra. A troca vale para um personagem quando ele tem pelo menos o limite de linhas e dois terços das ocorrências;
/// senão, vale para o projeto quando o total chega ao limite.
/// </remarks>
public static class CorrectionLearner
{
    /// <summary>Quantas linhas com a mesma troca bastam para oferecer a regra.</summary>
    public const int DefaultThreshold = 3;

    /// <summary>O maior trecho, em palavras, de cada lado de uma troca.</summary>
    public const int MaxWords = 3;

    /// <summary>Uma linha com mais trocas que isto foi reescrita, não corrigida.</summary>
    public const int MaxSwapsPerLine = 3;

    /// <summary>Linhas muito longas não entram: a comparação é quadrática no número de palavras.</summary>
    private const int MaxLineWords = 200;

    /// <summary>As trocas de palavras que levam <paramref name="ai"/> a <paramref name="edited"/>, na ordem do texto. Vazio quando não há troca, ou quando a linha foi reescrita.</summary>
    public static IReadOnlyList<WordSwap> Swaps(string? ai, string? edited)
    {
        if (string.IsNullOrWhiteSpace(ai) || string.IsNullOrWhiteSpace(edited) || string.Equals(ai, edited, StringComparison.Ordinal))
        {
            return [];
        }

        var a = Words(ai);
        var b = Words(edited);
        if (a.Count > MaxLineWords || b.Count > MaxLineWords)
        {
            return [];
        }

        // O começo e o fim iguais não trazem troca; tirá-los deixa a comparação quadrática só para o meio que mudou.
        var head = 0;
        while (head < a.Count && head < b.Count && Same(a[head], b[head]))
        {
            head++;
        }

        var tail = 0;
        while (tail < a.Count - head && tail < b.Count - head && Same(a[a.Count - 1 - tail], b[b.Count - 1 - tail]))
        {
            tail++;
        }

        var swaps = Hunks(a.GetRange(head, a.Count - head - tail), b.GetRange(head, b.Count - head - tail));
        return swaps is null || swaps.Count > MaxSwapsPerLine ? [] : swaps;
    }

    /// <summary>
    /// As trocas repetidas nas linhas dadas, as mais frequentes primeiro. Só entram linhas com tradução gravada e com a tradução da IA guardada.
    /// </summary>
    /// <param name="lines">As linhas do projeto, com a chave do script.</param>
    /// <param name="threshold">Quantas linhas bastam; no mínimo 2.</param>
    public static IReadOnlyList<RepeatedCorrection> Find(IEnumerable<(string ScriptKey, TextUnit Unit)> lines, int threshold = DefaultThreshold)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 2);

        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        foreach (var (key, unit) in lines)
        {
            if (unit.AiTranslation is null || !unit.HasTranslation)
            {
                continue;
            }

            var speaker = string.IsNullOrWhiteSpace(unit.EffectiveSpeaker) ? null : unit.EffectiveSpeaker.Trim();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var swap in Swaps(unit.AiTranslation, unit.Translation))
            {
                var swapKey = $"{swap.From.ToLowerInvariant()}|{swap.To.ToLowerInvariant()}";
                if (!seen.Add(swapKey))
                {
                    continue;
                }

                if (!groups.TryGetValue(swapKey, out var group))
                {
                    groups[swapKey] = group = new Group(swap);
                }

                group.Add(speaker, (key, unit.Id));
            }
        }

        var found = new List<RepeatedCorrection>();
        foreach (var group in groups.Values)
        {
            if (group.Offer(threshold) is { } correction)
            {
                found.Add(correction);
            }
        }

        return [.. found.OrderByDescending(c => c.Count).ThenBy(c => c.Swap.From, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Swap.To, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Speaker, StringComparer.Ordinal)];
    }

    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inWord = i < text.Length && IsWordChar(text, i);
            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                words.Add(text[start..i]);
                start = -1;
            }
        }

        return words;
    }

    private static bool IsWordChar(string text, int i)
    {
        var c = text[i];
        if (char.IsLetterOrDigit(c))
        {
            return true;
        }

        // Apóstrofo e hífen só ligam letras (d'água, guarda-chuva); sozinhos são pontuação.
        return (c is '\'' or '’' or '-') && i > 0 && i + 1 < text.Length && char.IsLetterOrDigit(text[i - 1]) && char.IsLetterOrDigit(text[i + 1]);
    }

    /// <summary>As trocas do diff palavra a palavra; nulo quando algum trecho trocado é grande demais para ser troca.</summary>
    private static List<WordSwap>? Hunks(List<string> a, List<string> b)
    {
        var table = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
        {
            for (var j = b.Count - 1; j >= 0; j--)
            {
                table[i, j] = Same(a[i], b[j]) ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var swaps = new List<WordSwap>();
        List<string> removed = [];
        List<string> added = [];
        int x = 0, y = 0;
        while (x < a.Count || y < b.Count)
        {
            if (x < a.Count && y < b.Count && Same(a[x], b[y]))
            {
                if (!Flush(swaps, removed, added))
                {
                    return null;
                }

                x++;
                y++;
            }
            else if (y >= b.Count || (x < a.Count && table[x + 1, y] >= table[x, y + 1]))
            {
                removed.Add(a[x++]);
            }
            else
            {
                added.Add(b[y++]);
            }
        }

        return Flush(swaps, removed, added) ? swaps : null;
    }

    private static bool Flush(List<WordSwap> swaps, List<string> removed, List<string> added)
    {
        // Uma troca grande é reescrita e anula a linha; um acréscimo ou corte puro, mesmo longo, só não conta como troca.
        var big = removed.Count > 0 && added.Count > 0 && (removed.Count > MaxWords || added.Count > MaxWords);
        if (!big && removed.Count > 0 && added.Count > 0)
        {
            var swap = new WordSwap(string.Join(' ', removed), string.Join(' ', added));
            if (!string.Equals(swap.From, swap.To, StringComparison.OrdinalIgnoreCase))
            {
                swaps.Add(swap);
            }
        }

        removed.Clear();
        added.Clear();
        return !big;
    }

    private static bool Same(string x, string y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

    private sealed class Group(WordSwap first)
    {
        private readonly Dictionary<string, List<(string ScriptKey, string UnitId)>> bySpeaker = new(StringComparer.Ordinal);
        private readonly List<(string ScriptKey, string UnitId)> all = [];

        public void Add(string? speaker, (string ScriptKey, string UnitId) line)
        {
            all.Add(line);
            if (speaker is null)
            {
                return;
            }

            if (!bySpeaker.TryGetValue(speaker, out var list))
            {
                bySpeaker[speaker] = list = [];
            }

            list.Add(line);
        }

        public RepeatedCorrection? Offer(int threshold)
        {
            if (all.Count < threshold)
            {
                return null;
            }

            var top = bySpeaker.OrderByDescending(p => p.Value.Count).ThenBy(p => p.Key, StringComparer.Ordinal).FirstOrDefault();
            return top.Value is { Count: var n } && n >= threshold && n * 3 >= all.Count * 2
                ? new RepeatedCorrection(CorrectionScope.Character, top.Key, first, top.Value)
                : new RepeatedCorrection(CorrectionScope.Project, null, first, all);
        }
    }
}
