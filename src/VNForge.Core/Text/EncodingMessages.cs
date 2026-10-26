using System.Globalization;

namespace VNForge.Core.Text;

/// <summary>Trechos de mensagem de erro de codificação: o caractere recusado pelo nome e o texto em volta, para a pessoa achar o ponto sem decodificar um <c>U+XXXX</c>.</summary>
public static class EncodingMessages
{
    /// <summary>O caractere de <paramref name="text"/> em <paramref name="index"/> (par substituto conta um), no formato <c>o caractere '犬' (U+72AC)</c>.</summary>
    public static string Character(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, text.Length);

        var length = index + 1 < text.Length && char.IsSurrogatePair(text[index], text[index + 1]) ? 2 : 1;
        // Substituto solto não forma um Rune: usa o valor do próprio char.
        var codePoint = length == 2 ? char.ConvertToUtf32(text[index], text[index + 1]) : text[index];
        return $"o caractere '{text.Substring(index, length)}' (U+{codePoint.ToString("X4", CultureInfo.InvariantCulture)})";
    }

    /// <summary>Até <paramref name="radius"/> caracteres de cada lado de <paramref name="index"/>, com reticências onde o texto continua.</summary>
    public static string Excerpt(string text, int index, int radius = 12)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, text.Length);

        var start = Math.Max(0, index - radius);
        var end = Math.Min(text.Length, index + radius + 1);
        return $"{(start > 0 ? "…" : string.Empty)}{text[start..end]}{(end < text.Length ? "…" : string.Empty)}";
    }
}
