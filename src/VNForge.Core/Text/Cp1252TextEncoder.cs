using System.Globalization;
using System.Text;

namespace VNForge.Core.Text;

/// <summary>
/// Estratégia <c>cp1252</c>: grava o texto exibido em Windows-1252, um byte por letra acentuada.
/// O byte <c>0xEF</c> (<c>ï</c>) é o prefixo dos códigos de controle do YU-RIS, então <c>ï</c> é recusado.
/// Caractere fora da página 1252 não é substituído nem descartado: <see cref="Encode"/> lança exceção.
/// </summary>
public sealed class Cp1252TextEncoder : ITextEncoder
{
    private const char ControlPrefixLetter = 'ï';
    private static readonly Encoding Strict = CreateStrict();

    public string Id => "cp1252";

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
        if (rune.Value == ControlPrefixLetter)
        {
            return false;
        }

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
        var clone = (Encoding)GameEncodings.Windows1252.Clone();
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
        string reason = invalid.Any(i => text[i] == ControlPrefixLetter)
            ? " O ï vira o byte 0xEF, o prefixo dos códigos de controle."
            : string.Empty;
        return $"O texto tem {invalid.Count} caractere(s) que a estratégia cp1252 não grava; o primeiro está na posição {first} (U+{shown}).{reason}";
    }
}
