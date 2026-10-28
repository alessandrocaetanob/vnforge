using VNForge.Core.Qa;
using WeCantSpell.Hunspell;

namespace VNForge.Core.Spelling;

/// <summary>
/// Corretor de português do VNForge: o dicionário Hunspell pt-BR embutido (VERO, ver <c>THIRD-PARTY-NOTICES.md</c>) lido por WeCantSpell.Hunspell, mais o
/// <see cref="ProjectDictionary"/> do projeto. Só lê o texto da tradução: marcadores de controle (<c>\n</c>, <c>\p</c>, <c>\c</c>, <c>{...}</c>) ficam de fora e uma
/// palavra colada a um marcador não é conferida, porque o pedaço visível pode estar incompleto.
/// </summary>
/// <remarks>
/// O que não vira erro, de propósito: inicial maiúscula no meio da frase (nome próprio, em geral romaji), sigla em maiúsculas, letra repetida três vezes ou mais
/// (alongamento: "nããão"), letra solta, gaguejo ("S-sim") e honoríficos ("-san", "-kun"). Palavra com inicial maiúscula no começo da frase só passa se o dicionário
/// do projeto (glossário e personagens) a conhece, senão "Nao" no lugar de "Não" escaparia.
/// </remarks>
public sealed class SpellChecker
{
    /// <summary>Caractere que substitui, na cópia do texto, tudo que é marcador: nenhuma palavra o atravessa e quem encosta nele não é conferida.</summary>
    private const char Blocked = '\u0001';

    private const string OpeningPunctuation = "\"“”«»‘'([「『-—–";

    private const string SentenceEnd = ".!?…:";

    /// <summary>Honoríficos que o dicionário pt-BR não tem; um nome com inicial maiúscula colado a um deles ("Misaki-chan") é nome, mesmo no começo da frase.</summary>
    private static readonly HashSet<string> Honorifics = new(StringComparer.OrdinalIgnoreCase)
    {
        "san", "kun", "chan", "sama", "dono", "tan", "chin", "senpai", "sempai", "kouhai", "sensei", "onii", "onee", "nii", "nee", "oniisan", "oneesan",
    };

    /// <summary>Interjeições comuns de tradução de jogo que o dicionário pt-BR não tem.</summary>
    private static readonly HashSet<string> Interjections = new(StringComparer.OrdinalIgnoreCase)
    {
        "hmm", "hum", "ahn", "uhn", "ahá", "ué", "uau", "ufa", "aff", "nhe", "tsc", "ok", "okay",
    };

    private static readonly Lazy<SpellChecker> PortugueseChecker = new(LoadPortuguese, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly WordList dictionary;

    /// <summary>Corretor sobre um dicionário já carregado.</summary>
    public SpellChecker(WordList dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        this.dictionary = dictionary;
    }

    /// <summary>O corretor com o dicionário pt-BR embutido. A primeira chamada lê o dicionário (cerca de um segundo); use <see cref="WarmUpAsync"/> fora da tela.</summary>
    public static SpellChecker Portuguese => PortugueseChecker.Value;

    /// <summary>O dicionário pt-BR já foi lido: <see cref="Portuguese"/> responde na hora.</summary>
    public static bool IsPortugueseReady => PortugueseChecker.IsValueCreated;

    /// <summary>Lê o dicionário pt-BR em segundo plano, para a primeira conferência não travar quem a pediu.</summary>
    public static Task WarmUpAsync() => Task.Run(() => Portuguese);

    /// <summary>Cada palavra de <paramref name="text"/> que não está no dicionário nem no <paramref name="project"/>, na ordem do texto, com o índice no texto inteiro.</summary>
    public IReadOnlyList<QaSpan> Find(string text, ProjectDictionary? project = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        project ??= ProjectDictionary.Empty;

        var spans = new List<QaSpan>();
        foreach (var token in Tokenize(text))
        {
            if (!Accepts(token, project))
            {
                spans.Add(new QaSpan(token.Start, token.Text.Length, token.Text));
            }
        }

        return spans;
    }

    private static SpellChecker LoadPortuguese()
    {
        var assembly = typeof(SpellChecker).Assembly;
        using var dic = assembly.GetManifestResourceStream("VNForge.Core.Spelling.pt_BR.dic") ?? throw new VNForgeException("O dicionário pt-BR do corretor não está embutido neste build.");
        using var aff = assembly.GetManifestResourceStream("VNForge.Core.Spelling.pt_BR.aff") ?? throw new VNForgeException("O dicionário pt-BR do corretor não está embutido neste build.");
        return new SpellChecker(WordList.CreateFromStreams(dic, aff));
    }

    private bool Accepts(Token token, ProjectDictionary project)
    {
        var word = token.Text;
        if (IsAllUpper(word) || HasRun(word, 3) || IsKnown(word, project))
        {
            return true;
        }

        // Nome próprio no meio da frase: o dicionário não tem como saber, e apontar cada "Takeshi" só ensina a ignorar o sublinhado.
        if (!token.AtSentenceStart && char.IsUpper(word[0]))
        {
            return true;
        }

        var parts = word.Split('-', '\'', '’');
        if (parts.Length < 2)
        {
            return false;
        }

        if (char.IsUpper(parts[0][0]) && Honorifics.Contains(parts[^1]))
        {
            return true;
        }

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var stutter = i + 1 < parts.Length && parts[i + 1].StartsWith(part, StringComparison.OrdinalIgnoreCase);
            if (part.Length >= 2 && !stutter && !IsKnown(part, project))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsKnown(string word, ProjectDictionary project)
    {
        var plain = word.Replace('’', '\'');
        return plain.Length < 2 || project.Contains(plain) || Honorifics.Contains(plain) || Interjections.Contains(plain) || dictionary.Check(plain);
    }

    private static bool IsAllUpper(string word) => word.Length >= 2 && word.All(c => !char.IsLetter(c) || char.IsUpper(c));

    private static bool HasRun(string word, int length)
    {
        var run = 1;
        for (var i = 1; i < word.Length; i++)
        {
            run = char.ToLowerInvariant(word[i]) == char.ToLowerInvariant(word[i - 1]) && char.IsLetter(word[i]) ? run + 1 : 1;
            if (run >= length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cópia do texto com o mesmo comprimento onde marcador não é letra: <c>\n</c> vira espaço (a frase continua na linha de baixo), <c>\p</c> vira ponto e espaço
    /// (a página nova começa uma frase) e os demais marcadores, e qualquer barra ou chave solta, viram <see cref="Blocked"/>.
    /// </summary>
    private static char[] Flatten(string text)
    {
        var flat = text.ToCharArray();
        var i = 0;
        while (i < flat.Length)
        {
            if (flat[i] == '\\')
            {
                var next = i + 1 < flat.Length ? flat[i + 1] : '\0';
                switch (next)
                {
                    case 'n':
                        flat[i] = flat[i + 1] = ' ';
                        i += 2;
                        break;
                    case 'p':
                        flat[i] = '.';
                        flat[i + 1] = ' ';
                        i += 2;
                        break;
                    case 'c' or 'u' or '\\' or '{':
                        flat[i] = flat[i + 1] = Blocked;
                        i += 2;
                        break;
                    default:
                        flat[i] = Blocked;
                        i++;
                        break;
                }
            }
            else if (flat[i] == '{')
            {
                var close = text.IndexOf('}', i);
                var end = close < 0 ? i : close;
                Array.Fill(flat, Blocked, i, end - i + 1);
                i = end + 1;
            }
            else
            {
                i++;
            }
        }

        return flat;
    }

    private static List<Token> Tokenize(string text)
    {
        var flat = Flatten(text);
        var tokens = new List<Token>();
        var i = 0;
        while (i < flat.Length)
        {
            if (!char.IsLetter(flat[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < flat.Length && (char.IsLetter(flat[i]) || (flat[i] is '-' or '\'' or '’' && i + 1 < flat.Length && char.IsLetter(flat[i + 1]))))
            {
                i++;
            }

            var touchesMarker = (start > 0 && flat[start - 1] == Blocked) || (i < flat.Length && flat[i] == Blocked);
            var latin = text[start..i].All(c => c <= 'ɏ' || c == '’');
            if (!touchesMarker && latin)
            {
                tokens.Add(new Token(start, text.Substring(start, i - start), StartsSentence(flat, start)));
            }
        }

        return tokens;
    }

    private static bool StartsSentence(char[] flat, int start)
    {
        var j = start - 1;
        while (j >= 0 && (char.IsWhiteSpace(flat[j]) || OpeningPunctuation.Contains(flat[j])))
        {
            j--;
        }

        return j < 0 || SentenceEnd.Contains(flat[j]);
    }

    private readonly record struct Token(int Start, string Text, bool AtSentenceStart);
}
