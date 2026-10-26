using System.Text;
using VNForge.Core.Formatting;

namespace VNForge.Core.Text;

/// <summary>
/// A pontuação japonesa que a IA costuma copiar do original (<c>。 、 ～ （ ）「」</c>) e a do teclado que a substitui. Serve a duas coisas: o corretor da
/// regra de caractere não codificável, que só troca o que a estratégia do projeto não grava, e a opção "Trocar pontuação japonesa pela do teclado", que
/// troca tudo o que a tabela cobre na tradução que a IA devolve e na que a pessoa grava. Os marcadores de controle (<c>\n</c>, <c>{...}</c>) nunca são tocados.
/// </summary>
/// <remarks>
/// Cada caractere tem uma lista de substitutos em ordem de preferência; vale o primeiro que a estratégia grava. Sem substituto gravável, o caractere fica
/// como está: o erro continua visível em vez de virar outro texto. Reaproveita o princípio do <see cref="CharacterFolder"/> (tabela de string para string,
/// conferida contra o encoder), mas a tabela é outra: lá o alvo é o Shift-JIS, aqui é qualquer estratégia do projeto.
/// </remarks>
public static class JapanesePunctuation
{
    private const int FullwidthStart = 0xFF01;
    private const int FullwidthEnd = 0xFF5E;
    private const int FullwidthOffset = 0xFEE0;

    private const string Triple = "・・・";
    private const string JapaneseQuotes = "aspas japonesas";
    private const string JapaneseBracket = "colchete japonês";

    // Barra invertida e chaves formariam marcador de controle se o texto trocado fosse relido: ficam como estão.
    private static readonly HashSet<int> NotFolded = [0xFF3C, 0xFF5B, 0xFF5D];

    private static readonly Dictionary<int, string[]> Table = new()
    {
        [0x3002] = ["."],
        [0x3001] = [","],
        [0x30FB] = ["·", " "],
        [0x300C] = ["\""], [0x300D] = ["\""],
        [0x300E] = ["\""], [0x300F] = ["\""],
        [0x301D] = ["\""], [0x301F] = ["\""],
        [0x3010] = ["["], [0x3011] = ["]"],
        [0x3014] = ["("], [0x3015] = [")"],
        [0x301C] = ["~"],
        [0x2025] = [".."],
        [0x3000] = [" "],
        [0xFF61] = ["."], [0xFF64] = [","],
        [0xFF62] = ["\""], [0xFF63] = ["\""],
        [0xFF65] = ["·", " "],
    };

    // Só entra quando a estratégia não grava o original: o … é pontuação ocidental comum e a opção não o mexe.
    private static readonly Dictionary<int, string[]> OnlyWhenUnwritable = new()
    {
        [0x2026] = ["..."],
    };

    private static readonly Dictionary<int, string> Names = new()
    {
        [0x3002] = "ponto final japonês",
        [0x3001] = "vírgula japonesa",
        [0x30FB] = "ponto médio japonês",
        [0x300C] = JapaneseQuotes, [0x300D] = JapaneseQuotes,
        [0x300E] = JapaneseQuotes, [0x300F] = JapaneseQuotes,
        [0x301D] = JapaneseQuotes, [0x301F] = JapaneseQuotes,
        [0x3010] = JapaneseBracket, [0x3011] = JapaneseBracket,
        [0x3014] = JapaneseBracket, [0x3015] = JapaneseBracket,
        [0x301C] = "til japonês",
        [0xFF5E] = "til japonês",
        [0x2025] = "reticências de dois pontos",
        [0x2026] = "reticências",
        [0x3000] = "espaço de largura dupla",
        [0xFF61] = "ponto final japonês", [0xFF64] = "vírgula japonesa",
        [0xFF62] = JapaneseQuotes, [0xFF63] = JapaneseQuotes,
        [0xFF65] = "ponto médio japonês",
        [0xFF01] = "ponto de exclamação de largura dupla",
        [0xFF1F] = "ponto de interrogação de largura dupla",
        [0xFF08] = "parêntese de largura dupla", [0xFF09] = "parêntese de largura dupla",
        [0xFF0C] = "vírgula de largura dupla",
        [0xFF0E] = "ponto de largura dupla",
        [0xFF1A] = "dois-pontos de largura dupla",
        [0xFF1B] = "ponto e vírgula de largura dupla",
    };

    /// <summary>
    /// Troca tudo o que a tabela cobre pela pontuação do teclado, com o primeiro substituto que <paramref name="encoder"/> grava (sem encoder, o primeiro da lista).
    /// É o que a opção do projeto aplica. Caractere sem substituto gravável, e o <c>…</c>, ficam como estão. Idempotente.
    /// </summary>
    public static string ToKeyboard(string text, ITextEncoder? encoder = null) => Convert(text, encoder, onlyUnwritable: false);

    /// <summary>
    /// Troca só os caracteres da tabela que <paramref name="encoder"/> não grava (<c>。</c> numa estratégia <c>cp1252</c>), e só quando o substituto é gravável.
    /// É o corretor da regra de caractere não codificável. Idempotente.
    /// </summary>
    public static string FixUnencodable(string text, ITextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        return Convert(text, encoder, onlyUnwritable: true);
    }

    /// <summary>O nome do caractere em português quando é pontuação ou letra de largura dupla ("til japonês"); nulo para qualquer outro.</summary>
    public static string? NameOf(int codePoint)
    {
        if (Names.TryGetValue(codePoint, out var name))
        {
            return name;
        }

        return codePoint switch
        {
            >= 0xFF21 and <= 0xFF3A or >= 0xFF41 and <= 0xFF5A => "letra de largura dupla",
            >= 0xFF10 and <= 0xFF19 => "dígito de largura dupla",
            >= FullwidthStart and <= FullwidthEnd when !NotFolded.Contains(codePoint) => "sinal de largura dupla",
            _ => null,
        };
    }

    /// <summary>
    /// O substituto preferido de <paramref name="codePoint"/>, o que o corretor usa quando a estratégia o grava (<c>~</c> para <c>～</c>); nulo quando a tabela não cobre
    /// o caractere. É o que a mensagem da regra e a Conferência sugerem.
    /// </summary>
    public static string? KeyboardFor(int codePoint) => Candidates(codePoint)?[0];

    private static string[]? Candidates(int codePoint)
    {
        if (Table.TryGetValue(codePoint, out var found) || OnlyWhenUnwritable.TryGetValue(codePoint, out found))
        {
            return found;
        }

        return codePoint is >= FullwidthStart and <= FullwidthEnd && !NotFolded.Contains(codePoint)
            ? [((char)(codePoint - FullwidthOffset)).ToString()]
            : null;
    }

    private static string Convert(string text, ITextEncoder? encoder, bool onlyUnwritable)
    {
        ArgumentNullException.ThrowIfNull(text);

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
            output.Append(segment.IsMarker ? segment.Text : ConvertPlain(segment.Text, encoder, onlyUnwritable));
        }

        return output.ToString();
    }

    private static string ConvertPlain(string text, ITextEncoder? encoder, bool onlyUnwritable)
    {
        var output = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            string original;
            string[]? candidates;
            if (string.CompareOrdinal(text, i, Triple, 0, Triple.Length) == 0)
            {
                original = Triple;
                candidates = ["…", "..."];
            }
            else
            {
                original = text[i].ToString();
                candidates = Candidates(text[i]);
                if (!onlyUnwritable && OnlyWhenUnwritable.ContainsKey(text[i]))
                {
                    candidates = null;
                }
            }

            var substitute = candidates is null ? null : Choose(original, candidates, encoder, onlyUnwritable);
            output.Append(substitute ?? original);
            i += original.Length;
        }

        return output.ToString();
    }

    private static string? Choose(string original, string[] candidates, ITextEncoder? encoder, bool onlyUnwritable)
    {
        if (onlyUnwritable && (encoder is null || encoder.FindUnencodable(original).Count == 0))
        {
            return null;
        }

        return candidates.FirstOrDefault(c => encoder is null || encoder.FindUnencodable(c).Count == 0);
    }
}
