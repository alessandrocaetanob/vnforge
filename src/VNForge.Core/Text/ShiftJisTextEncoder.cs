using System.Globalization;
using System.Text;

namespace VNForge.Core.Text;

/// <summary>
/// Estratégia <c>sjis</c>: converte direto para Shift-JIS (página 932), sem tratar acentos do português.
/// Caractere fora da página 932 não é substituído nem descartado: <see cref="Encode"/> lança exceção.
/// </summary>
public sealed class ShiftJisTextEncoder : ITextEncoder
{
    private static readonly Encoding Strict = CreateStrict();

    public string Id => "sjis";

    public byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        IReadOnlyList<int> invalid = FindUnencodable(text);
        if (invalid.Count > 0)
        {
            throw new EncoderFallbackException(Describe(text, invalid));
        }

        return Strict.GetBytes(text);
    }

    public IReadOnlyList<int> FindUnencodable(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = new List<int>();
        int index = 0;
        while (index < text.Length)
        {
            var status = Rune.DecodeFromUtf16(text.AsSpan(index), out Rune rune, out int consumed);
            if (status != System.Buffers.OperationStatus.Done || !CanEncode(rune))
            {
                result.Add(index);
            }

            index += Math.Max(consumed, 1);
        }

        return result;
    }

    private static bool CanEncode(Rune rune)
    {
        Span<char> chars = stackalloc char[2];
        int length = rune.EncodeToUtf16(chars);
        try
        {
            Strict.GetByteCount(chars[..length]);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static Encoding CreateStrict()
    {
        var clone = (Encoding)GameEncodings.ShiftJis.Clone();
        clone.EncoderFallback = EncoderFallback.ExceptionFallback;
        clone.DecoderFallback = DecoderFallback.ExceptionFallback;
        return clone;
    }

    private static string Describe(string text, IReadOnlyList<int> invalid)
    {
        int first = invalid[0];
        string shown = char.IsHighSurrogate(text[first]) && first + 1 < text.Length && char.IsLowSurrogate(text[first + 1])
            ? char.ConvertToUtf32(text[first], text[first + 1]).ToString("X4", CultureInfo.InvariantCulture)
            : ((int)text[first]).ToString("X4", CultureInfo.InvariantCulture);
        return $"O texto tem {invalid.Count} caractere(s) que o Shift-JIS não representa; o primeiro está na posição {first} (U+{shown}).";
    }
}
