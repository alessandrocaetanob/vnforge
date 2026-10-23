using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Aspas ou parênteses que abrem e não fecham (ou o contrário) na tradução. Uma fala que abre numa linha e fecha na seguinte é comum, então
/// a regra só avisa quando o original tem aquele grupo (aspas, parênteses) em ordem: se o original já está partido, a tradução pode estar também.
/// </summary>
public sealed class UnbalancedPairsRule : UnitQaRule
{
    private enum Group
    {
        Quotes,
        Parentheses,
    }

    private sealed record Pair(char Open, char Close, Group Group);

    // Nas aspas retas Open == Close: não há como saber quem abre, então a primeira abre e a seguinte fecha.
    private static readonly Pair[] Pairs =
    [
        new('"', '"', Group.Quotes),
        new('“', '”', Group.Quotes),
        new('«', '»', Group.Quotes),
        new('「', '」', Group.Quotes),
        new('『', '』', Group.Quotes),
        new('(', ')', Group.Parentheses),
        new('（', '）', Group.Parentheses),
    ];

    /// <inheritdoc />
    public override string Id => "aspas-desbalanceadas";

    /// <inheritdoc />
    public override string Description => "Aspas ou parênteses sem o par que abre ou fecha.";

    /// <inheritdoc />
    public override QaSeverity Severity => QaSeverity.Warning;

    /// <summary>Cada caractere sem par de <paramref name="translation"/>, na ordem do texto.</summary>
    public static IReadOnlyList<QaSpan> Find(string translation)
    {
        ArgumentNullException.ThrowIfNull(translation);
        return [.. FindUnmatched(translation).OrderBy(u => u.Index).Select(u => TextQaRules.Span(translation, u.Index, 1))];
    }

    /// <inheritdoc />
    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        if (!unit.HasTranslation)
        {
            return [];
        }

        var unmatched = FindUnmatched(unit.Translation!);
        if (unmatched.Count == 0)
        {
            return [];
        }

        var sourceBroken = FindUnmatched(unit.Source).Select(u => u.Group).ToHashSet();
        return unmatched
            .Where(u => !sourceBroken.Contains(u.Group))
            .OrderBy(u => u.Index)
            .Select(u => $"{(u.Group == Group.Quotes ? "Aspas" : "Parêntese")} '{unit.Translation![u.Index]}' sem par na posição {u.Index}.");
    }

    private static List<(int Index, Group Group)> FindUnmatched(string text)
    {
        var chars = TextQaRules.PlainRuns(text)
            .SelectMany(run => run.Text.Select((c, i) => (Index: run.Offset + i, Char: c)))
            .ToList();
        var unmatched = new List<(int, Group)>();
        foreach (var pair in Pairs)
        {
            unmatched.AddRange(FindUnmatched(chars, pair).Select(index => (index, pair.Group)));
        }

        return unmatched;
    }

    private static IEnumerable<int> FindUnmatched(List<(int Index, char Char)> chars, Pair pair)
    {
        var open = new Stack<int>();
        var stray = new List<int>();
        foreach (var (index, c) in chars)
        {
            if (c == pair.Open && (pair.Open != pair.Close || open.Count == 0))
            {
                open.Push(index);
            }
            else if (c == pair.Close && open.Count > 0)
            {
                open.Pop();
            }
            else if (c == pair.Close)
            {
                stray.Add(index);
            }
        }

        return [.. stray, .. open];
    }
}
