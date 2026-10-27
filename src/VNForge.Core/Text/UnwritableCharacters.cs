using System.Text;
using VNForge.Core.Formatting;

namespace VNForge.Core.Text;

/// <summary>
/// O corretor da regra de caractere não codificável: troca cada caractere que a estratégia do projeto não grava por um substituto só ASCII
/// (aspas curvas por retas, <c>…</c> por <c>...</c>, <c>♪</c> por <c>~</c>, letra acentuada fora da página pela letra sem o acento). A tabela está
/// no doc 06, seção "Acentos e fonte". Os marcadores de controle (<c>\n</c>, <c>{...}</c>) nunca são tocados.
/// </summary>
/// <remarks>
/// Só troca quando o substituto é gravável pelo encoder, e caractere fora da tabela fica como está: o erro continua visível para a pessoa decidir.
/// A pontuação japonesa que a tabela de <see cref="JapanesePunctuation"/> cobre e esta não (<c>。 、 【 】</c>) segue a tabela dela. Idempotente,
/// porque todo substituto é ASCII.
/// </remarks>
public static class UnwritableCharacters
{
    private const string Triple = "・・・";

    private static readonly Dictionary<int, string> Table = Build();

    /// <summary>A <paramref name="text"/> com os caracteres que <paramref name="encoder"/> não grava trocados pelo substituto da tabela, quando ele é gravável.</summary>
    public static string Fix(string text, ITextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoder);

        IReadOnlyList<TextSegment> segments;
        try
        {
            segments = ControlMarkers.Split(text);
        }
        catch (ArgumentException)
        {
            // Marcador inválido: a regra de marcador é que aponta; trocar em volta dele poderia esconder o defeito.
            return text;
        }

        var output = new StringBuilder(text.Length);
        foreach (var segment in segments)
        {
            output.Append(segment.IsMarker ? segment.Text : FixPlain(segment.Text, encoder));
        }

        return output.ToString();
    }

    /// <summary>
    /// O substituto que <see cref="Fix"/> usaria para <paramref name="codePoint"/>; nulo quando o encoder já grava o caractere ou quando não há substituto
    /// gravável. É o que a mensagem da regra sugere.
    /// </summary>
    public static string? SubstituteFor(int codePoint, ITextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        return Rune.TryCreate(codePoint, out var rune) ? Substitute(rune.ToString(), encoder) : null;
    }

    private static string FixPlain(string text, ITextEncoder encoder)
    {
        var output = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (string.CompareOrdinal(text, i, Triple, 0, Triple.Length) == 0 && Substitute(Triple, encoder) is { } dots)
            {
                output.Append(dots);
                i += Triple.Length;
                continue;
            }

            var length = Rune.DecodeFromUtf16(text.AsSpan(i), out _, out var consumed) == System.Buffers.OperationStatus.Done ? consumed : 1;
            var original = text.Substring(i, length);
            output.Append(Substitute(original, encoder) ?? original);
            i += length;
        }

        return output.ToString();
    }

    private static string? Substitute(string original, ITextEncoder encoder)
    {
        if (encoder.FindUnencodable(original).Count == 0)
        {
            return null;
        }

        string? candidate = null;
        if (original == Triple)
        {
            candidate = "...";
        }
        else if (Rune.TryGetRuneAt(original, 0, out var rune))
        {
            candidate = Table.GetValueOrDefault(rune.Value) ?? FromJapanesePunctuation(original, encoder) ?? WithoutMarks(original, encoder);
        }

        return candidate is not null && candidate != original && encoder.FindUnencodable(candidate).Count == 0 ? candidate : null;
    }

    private static string? FromJapanesePunctuation(string original, ITextEncoder encoder)
    {
        var fixedText = JapanesePunctuation.FixUnencodable(original, encoder);
        return fixedText == original ? null : fixedText;
    }

    /// <summary>
    /// A letra sem as marcas de acento (<c>ő</c> vira <c>o</c>); nulo quando o caractere não é uma letra com acento. Só vale para a estratégia que grava os
    /// acentos comuns (<c>é</c>): numa que não grava (<c>sjis</c>), tirar o acento de <c>ação</c> seria a estratégia <c>fold</c> sem a pessoa escolher.
    /// </summary>
    private static string? WithoutMarks(string original, ITextEncoder encoder)
    {
        if (encoder.FindUnencodable("é").Count > 0)
        {
            return null;
        }

        var decomposed = original.Normalize(NormalizationForm.FormD);
        var bare = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (c is < '̀' or > 'ͯ')
            {
                bare.Append(c);
            }
        }

        return bare.Length > 0 && bare.Length < decomposed.Length && bare.ToString() != original ? bare.ToString() : null;
    }

    private static Dictionary<int, string> Build()
    {
        var table = new Dictionary<int, string>();

        void Add(string substitute, params int[] codePoints)
        {
            foreach (var codePoint in codePoints)
            {
                table[codePoint] = substitute;
            }
        }

        Add("\"", 0x201C, 0x201D, 0x201E, 0x300C, 0x300D, 0x300E, 0x300F);
        Add("'", 0x2018, 0x2019, 0x201A, 0x2039, 0x203A);
        Add("...", 0x2026);
        Add("..", 0x2025);
        Add("-", 0x2013, 0x2014, 0x2015, 0x2500);
        Add("*", 0x2022, 0x2605, 0x2606, 0x203B);
        Add("TM", 0x2122);
        Add("S", 0x0160);
        Add("s", 0x0161);
        Add("Z", 0x017D);
        Add("z", 0x017E);
        Add("OE", 0x0152);
        Add("oe", 0x0153);
        Add("Y", 0x0178);
        Add("f", 0x0192);
        Add("^", 0x02C6);
        Add("~", 0x02DC, 0x266A, 0x266B, 0x2665, 0x2661, 0xFF5E, 0x301C);
        Add("i", 0x00EF);
        Add(" ", 0x30FB, 0xFF65, 0x3000);
        Add("->", 0x2192);
        Add("<-", 0x2190);
        return table;
    }
}
