using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Hiragana, katakana ou kanji que sobrou na tradução. Pontuação japonesa (<c>「」、。</c>) não conta. É aviso, não erro: um nome próprio ou o
/// título de uma obra pode ficar no original de propósito, e o revisor marca como "ok".
/// </summary>
public sealed class LeftoverJapaneseRule : UnitQaRule
{
    /// <inheritdoc />
    public override string Id => "japones-na-traducao";

    /// <inheritdoc />
    public override string Description => "Hiragana, katakana ou kanji esquecido na tradução.";

    /// <inheritdoc />
    public override QaSeverity Severity => QaSeverity.Warning;

    /// <summary>Cada sequência contínua de caracteres japoneses de <paramref name="translation"/>.</summary>
    public static IReadOnlyList<QaSpan> Find(string translation)
    {
        ArgumentNullException.ThrowIfNull(translation);
        var spans = new List<QaSpan>();
        foreach (var (offset, text) in TextQaRules.PlainRuns(translation))
        {
            var i = 0;
            while (i < text.Length)
            {
                if (!IsJapanese(text[i]))
                {
                    i++;
                    continue;
                }

                var end = i;
                while (end < text.Length && IsJapanese(text[end]))
                {
                    end++;
                }

                spans.Add(TextQaRules.Span(translation, offset + i, end - i));
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
            yield return $"Japonês na tradução: '{span.Text}' (posição {span.Start}).";
        }
    }

    // Kanji fora do plano básico (CJK estendido B em diante) usa par substituto e fica de fora: é raro em texto de jogo.
    private static bool IsJapanese(char c) =>
        c is (>= 'ぁ' and <= 'ゟ')
            or (>= 'ァ' and <= 'ヺ')
            or 'ー'
            or (>= '㐀' and <= '䶿')
            or (>= '一' and <= '鿿')
            or (>= '豈' and <= '﫿')
            or (>= 'ｦ' and <= 'ﾟ');
}
