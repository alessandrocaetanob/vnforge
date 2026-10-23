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
            yield break;
        }

        var unmatched = FindUnmatched(unit.Translation!).ToList();
        if (unmatched.Count == 0)
        {
            yield break;
        }

        var sourceBroken = FindUnmatched(unit.Source).Select(u => u.Group).ToHashSet();
        foreach (var (index, group) in unmatched.Where(u => !sourceBroken.Contains(u.Group)).OrderBy(u => u.Index))
        {
            var kind = group == Group.Quotes ? "Aspas" : "Parêntese";
            yield return $"{kind} '{unit.Translation![index]}' sem par na posição {index}.";
        }
    }

    private static IEnumerable<(int Index, Group Group)> FindUnmatched(string text)
    {
        var runs = TextQaRules.PlainRuns(text);
        foreach (var pair in Pairs)
        {
            var straight = pair.Open == pair.Close;
            var open = new Stack<int>();
            foreach (var (offset, run) in runs)
            {
                for (var i = 0; i < run.Length; i++)
                {
                    var c = run[i];
                    if (straight && c == pair.Open)
                    {
                        if (open.Count == 0)
                        {
                            open.Push(offset + i);
                        }
                        else
                        {
                            open.Pop();
                        }
                    }
                    else if (!straight && c == pair.Open)
                    {
                        open.Push(offset + i);
                    }
                    else if (!straight && c == pair.Close)
                    {
                        if (open.Count > 0)
                        {
                            open.Pop();
                        }
                        else
                        {
                            yield return (offset + i, pair.Group);
                        }
                    }
                }
            }

            foreach (var index in open)
            {
                yield return (index, pair.Group);
            }
        }
    }
}
