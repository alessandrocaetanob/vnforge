using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>Dois ou mais espaços seguidos (comum, NBSP ou ideográfico) na tradução. Um marcador entre os espaços os separa.</summary>
public sealed class DoubleSpaceRule : UnitQaRule, IQaFixer
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
                if (!IsSpace(text[i]))
                {
                    i++;
                    continue;
                }

                var end = i;
                while (end < text.Length && IsSpace(text[end]))
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

    /// <summary>Reduz a um espaço comum cada sequência só de espaços comuns. NBSP e espaço ideográfico ficam: podem ser deliberados.</summary>
    public string Fix(string translation)
    {
        var fixedText = new System.Text.StringBuilder(translation);
        foreach (var span in Find(translation).Reverse())
        {
            if (span.Text.All(c => c == ' '))
            {
                fixedText.Remove(span.Start + 1, span.Length - 1);
            }
        }

        return fixedText.ToString();
    }

    private static bool IsSpace(char c) => c is ' ' or ' ' or '　';

    /// <inheritdoc />
    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        return unit.HasTranslation ? Find(unit.Translation).Select(span => $"{span.Length} espaços seguidos na posição {span.Start}.") : [];
    }
}
