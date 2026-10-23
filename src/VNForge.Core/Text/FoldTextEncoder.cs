using System.Globalization;
using System.Text;

namespace VNForge.Core.Text;

/// <summary>
/// Estratégia <c>fold</c>: aplica a troca do <see cref="CharacterFolder"/> (acento removido, aspas e travessões trocados por
/// equivalentes) e codifica em Shift-JIS. O que mesmo depois da troca não cabe na página 932 continua recusado: <see cref="Encode"/>
/// lança exceção, como no <see cref="ShiftJisTextEncoder"/>.
/// </summary>
public sealed class FoldTextEncoder : IReplacingTextEncoder
{
    private readonly ShiftJisTextEncoder _sjis = new();

    public string Id => "fold";

    public byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        FoldResult folded = CharacterFolder.Fold(text);
        if (folded.Unresolved.Count > 0)
        {
            throw new EncoderFallbackException(Describe(text, folded.Unresolved));
        }

        return _sjis.Encode(folded.Text);
    }

    public string? GetReplacement(string character)
    {
        ArgumentNullException.ThrowIfNull(character);

        var replacements = CharacterFolder.Fold(character).Replacements;
        return replacements.Count == 1 ? replacements[0].Substitute : null;
    }

    public IReadOnlyList<int> FindUnencodable(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return CharacterFolder.Fold(text).Unresolved;
    }

    private static string Describe(string text, IReadOnlyList<int> invalid)
    {
        int first = invalid[0];
        int codePoint = char.IsHighSurrogate(text[first]) && first + 1 < text.Length && char.IsLowSurrogate(text[first + 1])
            ? char.ConvertToUtf32(text[first], text[first + 1])
            : text[first];
        return $"Mesmo depois de trocar acentos e sinais, o texto tem {invalid.Count} caractere(s) que o Shift-JIS não representa; o primeiro está na posição {first} (U+{codePoint.ToString("X4", CultureInfo.InvariantCulture)}).";
    }
}
