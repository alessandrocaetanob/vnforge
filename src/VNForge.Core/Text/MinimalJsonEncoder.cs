using System.Text.Encodings.Web;

namespace VNForge.Core.Text;

/// <summary>
/// Codificador de JSON que escapa só o que a gramática do JSON exige: aspas, barra invertida e U+0000 a U+001F.
/// O resto sai literal em UTF-8, inclusive o espaço ideográfico U+3000, o U+00A0 e emoji, que o
/// <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> escaparia (e viram <c>\u3000</c>, ilegível no diff do <c>text/*.json</c>).
/// Não depende da tabela Unicode do runtime. Surrogates soltos nunca chegam aqui: o <c>Utf8JsonWriter</c> recusa UTF-16 inválido antes.
/// </summary>
internal sealed unsafe class MinimalJsonEncoder : JavaScriptEncoder
{
    public override int MaxOutputCharactersPerInputCharacter => 6;

    public override bool WillEncode(int unicodeScalar) => NeedsEscape(unicodeScalar);

    public override int FindFirstCharacterToEncode(char* text, int textLength)
    {
        for (var i = 0; i < textLength; i++)
        {
            if (NeedsEscape(text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    public override bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten)
    {
        Span<char> escaped = stackalloc char[6];
        var length = Escape(unicodeScalar, escaped);
        if (length > bufferLength)
        {
            numberOfCharactersWritten = 0;
            return false;
        }

        for (var i = 0; i < length; i++)
        {
            buffer[i] = escaped[i];
        }

        numberOfCharactersWritten = length;
        return true;
    }

    private static bool NeedsEscape(int scalar) => scalar < 0x20 || scalar == '"' || scalar == '\\';

    private static int Escape(int scalar, Span<char> destination)
    {
        if (!NeedsEscape(scalar))
        {
            return new System.Text.Rune(scalar).EncodeToUtf16(destination);
        }

        destination[0] = '\\';
        switch (scalar)
        {
            case '"': destination[1] = '"'; return 2;
            case '\\': destination[1] = '\\'; return 2;
            case '\n': destination[1] = 'n'; return 2;
            case '\r': destination[1] = 'r'; return 2;
            case '\t': destination[1] = 't'; return 2;
            case '\b': destination[1] = 'b'; return 2;
            case '\f': destination[1] = 'f'; return 2;
        }

        destination[1] = 'u';
        scalar.TryFormat(destination[2..], out _, "X4");
        return 6;
    }
}
