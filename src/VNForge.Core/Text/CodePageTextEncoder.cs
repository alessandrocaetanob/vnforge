using System.Globalization;
using System.Text;

namespace VNForge.Core.Text;

/// <summary>
/// Base das estratégias que gravam o texto direto numa página de código. Caractere que a página não
/// representa não é substituído nem descartado: <see cref="Encode"/> lança <see cref="EncoderFallbackException"/>.
/// </summary>
public abstract class CodePageTextEncoder : ITextEncoder
{
    private readonly Encoding _strict;

    protected CodePageTextEncoder(Encoding encoding)
    {
        _strict = (Encoding)encoding.Clone();
        _strict.EncoderFallback = EncoderFallback.ExceptionFallback;
        _strict.DecoderFallback = DecoderFallback.ExceptionFallback;
    }

    public abstract string Id { get; }

    /// <summary>Nome da página nas mensagens de erro.</summary>
    protected abstract string DisplayName { get; }

    public byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        IReadOnlyList<int> invalid = FindUnencodable(text);
        if (invalid.Count > 0)
        {
            throw new EncoderFallbackException(Describe(text, invalid));
        }

        return _strict.GetBytes(text);
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

    /// <summary>Recusa extra além do que a página não representa.</summary>
    protected virtual bool IsRefused(Rune rune) => false;

    /// <summary>Complemento da mensagem de erro para os caracteres recusados.</summary>
    protected virtual string Hint(string text, IReadOnlyList<int> invalid) => string.Empty;

    private bool CanEncode(Rune rune)
    {
        if (IsRefused(rune))
        {
            return false;
        }

        Span<char> chars = stackalloc char[2];
        int length = rune.EncodeToUtf16(chars);
        try
        {
            _strict.GetByteCount(chars[..length]);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private string Describe(string text, IReadOnlyList<int> invalid)
    {
        int first = invalid[0];
        string shown = char.IsHighSurrogate(text[first]) && first + 1 < text.Length && char.IsLowSurrogate(text[first + 1])
            ? char.ConvertToUtf32(text[first], text[first + 1]).ToString("X4", CultureInfo.InvariantCulture)
            : ((int)text[first]).ToString("X4", CultureInfo.InvariantCulture);
        return $"O texto tem {invalid.Count} caractere(s) que {DisplayName}; o primeiro está na posição {first} (U+{shown}).{Hint(text, invalid)}";
    }
}
