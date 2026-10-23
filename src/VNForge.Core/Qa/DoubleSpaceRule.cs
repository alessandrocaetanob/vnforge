using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>Dois ou mais espaços seguidos na tradução. Um marcador entre os espaços os separa.</summary>
public sealed class DoubleSpaceRule : UnitQaRule
{
    /// <inheritdoc />
    public override string Id => "espaco-duplo";

    /// <inheritdoc />
    public override string Description => "Dois ou mais espaços seguidos na tradução.";

    /// <inheritdoc />
    public override QaSeverity Severity => QaSeverity.Warning;

    /// <summary>Cada sequência de dois ou mais espaços de <paramref name="translation"/>, no índice da tradução inteira.</summary>
    public static IReadOnlyList<QaSpan> Find(string translation)
    {
        ArgumentNullException.ThrowIfNull(translation);
        var spans = new List<QaSpan>();
        foreach (var (offset, text) in TextQaRules.PlainRuns(translation))
        {
            var i = 0;
            while (i < text.Length)
            {
                if (text[i] != ' ')
                {
                    i++;
                    continue;
                }

                var end = i;
                while (end < text.Length && text[end] == ' ')
                {
                    end++;
                }

                if (end - i >= 2)
                {
                    spans.Add(TextQaRules.Span(translation, offset + i, end - i));
                }

                i = end;
            }
        }

        return spans;
    }

    /// <inheritdoc />
    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        if (!unit.HasTranslation)
        {
            yield break;
        }

        foreach (var span in Find(unit.Translation!))
        {
            yield return $"{span.Length} espaços seguidos na posição {span.Start}.";
        }
    }
}
