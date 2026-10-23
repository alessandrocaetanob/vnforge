using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>Espaço antes de <c>, ; : ? !</c>. Reticências (<c>...</c>) ficam fora: abrir uma fala com elas depois de um espaço é correto.</summary>
public sealed class SpaceBeforePunctuationRule : UnitQaRule
{
    private const string Punctuation = ",;:?!";

    /// <inheritdoc />
    public override string Id => "espaco-antes-pontuacao";

    /// <inheritdoc />
    public override string Description => "Espaço antes de vírgula, ponto e vírgula, dois-pontos, interrogação ou exclamação.";

    /// <inheritdoc />
    public override QaSeverity Severity => QaSeverity.Warning;

    /// <summary>Cada sinal precedido de espaço, com os espaços: o trecho vai do primeiro espaço ao sinal.</summary>
    public static IReadOnlyList<QaSpan> Find(string translation)
    {
        ArgumentNullException.ThrowIfNull(translation);
        var spans = new List<QaSpan>();
        foreach (var (offset, text) in TextQaRules.PlainRuns(translation))
        {
            for (var i = 1; i < text.Length; i++)
            {
                if (!Punctuation.Contains(text[i]) || text[i - 1] != ' ')
                {
                    continue;
                }

                var start = i - 1;
                while (start > 0 && text[start - 1] == ' ')
                {
                    start--;
                }

                spans.Add(TextQaRules.Span(translation, offset + start, i - start + 1));
            }
        }

        return spans;
    }

    /// <inheritdoc />
    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        return unit.HasTranslation ? Find(unit.Translation!).Select(span => $"Espaço antes de '{span.Text[^1]}' na posição {span.Start}.") : [];
    }
}
