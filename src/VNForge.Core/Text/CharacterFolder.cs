using System.Text;

namespace VNForge.Core.Text;

/// <summary>Uma troca feita pelo <see cref="CharacterFolder"/>: o trecho original, o substituto e a posição no texto de entrada.</summary>
/// <param name="Index">Posição (em <c>char</c> do UTF-16) do trecho original no texto de entrada.</param>
/// <param name="Original">Caractere de entrada (um par substituto conta como um só).</param>
/// <param name="Substitute">Texto que entrou no lugar; vazio quando o caractere foi apenas removido (acento solto).</param>
public readonly record struct FoldReplacement(int Index, string Original, string Substitute);

/// <summary>Resultado de <see cref="CharacterFolder.Fold"/>.</summary>
/// <param name="Text">Texto depois da troca.</param>
/// <param name="Replacements">Cada troca feita, em ordem de posição no texto de entrada.</param>
/// <param name="Unresolved">Índices (no texto de entrada) dos caracteres que continuam fora do Shift-JIS depois da troca. Esses caracteres ficam no <see cref="Text"/> como estavam.</param>
public sealed record FoldResult(string Text, IReadOnlyList<FoldReplacement> Replacements, IReadOnlyList<int> Unresolved);

/// <summary>
/// A troca da estratégia <c>fold</c>, de string para string: tira acentos (<c>ação</c> vira <c>acao</c>) e troca por equivalentes os
/// sinais que a página 932 não tem (<c>«»</c>, <c>ª º</c>, travessões, <c>¿ ¡</c>...). Quem o Shift-JIS já representa passa sem troca.
/// </summary>
public static class CharacterFolder
{
    private static readonly ShiftJisTextEncoder Sjis = new();

    private static readonly Dictionary<string, string> Table = new()
    {
        ["«"] = "\"", ["»"] = "\"",
        ["‹"] = "'", ["›"] = "'",
        ["„"] = "\"", ["‚"] = "'",
        ["‛"] = "'", ["‟"] = "\"",
        ["ª"] = "a", ["º"] = "o",
        ["–"] = "-", ["—"] = "-", ["‑"] = "-", ["‒"] = "-",
        ["¿"] = "?", ["¡"] = "!",
        [" "] = " ", ["\u2009"] = " ", ["\u202F"] = " ",
        ["•"] = "*", ["·"] = ".",
        ["€"] = "EUR", ["©"] = "(c)", ["®"] = "(R)", ["™"] = "TM",
        ["½"] = "1/2", ["¼"] = "1/4", ["¾"] = "3/4",
        ["ß"] = "ss", ["æ"] = "ae", ["Æ"] = "AE", ["œ"] = "oe", ["Œ"] = "OE",
        ["ø"] = "o", ["Ø"] = "O", ["đ"] = "d", ["Đ"] = "D", ["ł"] = "l", ["Ł"] = "L",
        ["ð"] = "d", ["Ð"] = "D", ["þ"] = "th", ["Þ"] = "Th",
    };

    /// <summary>Troca o texto e relata o que trocou e o que ainda ficou fora do Shift-JIS.</summary>
    public static FoldResult Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var output = new StringBuilder(text.Length);
        var replacements = new List<FoldReplacement>();
        var unresolved = new List<int>();

        int index = 0;
        while (index < text.Length)
        {
            var status = Rune.DecodeFromUtf16(text.AsSpan(index), out Rune _, out int consumed);
            consumed = Math.Max(consumed, 1);
            string original = text.Substring(index, consumed);

            if (status == System.Buffers.OperationStatus.Done && Sjis.FindUnencodable(original).Count == 0)
            {
                output.Append(original);
            }
            else if (status == System.Buffers.OperationStatus.Done && TrySubstitute(original, out string? substitute))
            {
                output.Append(substitute);
                replacements.Add(new FoldReplacement(index, original, substitute));
            }
            else
            {
                output.Append(original);
                unresolved.Add(index);
            }

            index += consumed;
        }

        return new FoldResult(output.ToString(), replacements, unresolved);
    }

    private static bool TrySubstitute(string original, out string substitute)
    {
        if (Table.TryGetValue(original, out string? mapped) && Sjis.FindUnencodable(mapped).Count == 0)
        {
            substitute = mapped;
            return true;
        }

        // Letra com acento: decompõe e descarta as marcas. Marca combinante solta (texto já decomposto) também some.
        var stripped = new StringBuilder();
        // Só as marcas latinas (U+0300 a U+036F): a dakuten U+3099 ou vogais árabes não podem sumir em silêncio.
        // FormKD também resolve ², ³, ¹ e ligaturas.
        foreach (char c in original.Normalize(NormalizationForm.FormKD).Where(ch => ch is < '\u0300' or > '\u036F'))
        {
            stripped.Append(c);
        }

        substitute = stripped.ToString();
        return Sjis.FindUnencodable(substitute).Count == 0;
    }
}
