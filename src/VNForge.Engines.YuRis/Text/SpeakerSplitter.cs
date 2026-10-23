namespace VNForge.Engines.YuRis.Text;

/// <summary>
/// Uma mensagem no formato <c>nome「fala」</c> dividida: o original é <c>Name + Open + Body + Close</c>, sem sobra.
/// </summary>
/// <param name="Name">Texto antes da abertura, como está nos bytes (é o falante).</param>
/// <param name="Open">Abertura que fica fora do <paramref name="Body"/>: <c>「</c> ou <c>『</c>; vazia no formato <c>nome（fala）</c>.</param>
/// <param name="Close">Fechamento que fica fora do <paramref name="Body"/>: <c>」</c> ou <c>』</c>; vazio no formato <c>nome（fala）</c>.</param>
/// <param name="Body">A fala. No formato <c>nome（fala）</c> mantém os parênteses, como o VNTextPatch.</param>
internal readonly record struct SpeakerSplit(string Name, string Open, string Close, string Body);

/// <summary>
/// Separa o falante de uma mensagem do E-ris (<c>名前「fala」</c>), sobre o texto já com marcadores. É a regra do
/// VNTextPatch (<c>YurisScenarioScript.cs:79</c>: nome, abertura <c>「</c> ou <c>『</c> e fechamento <c>」</c> ou <c>』</c>, ou
/// o par <c>（ ）</c>), com um filtro a mais: o nome não pode ter pontuação de frase (<c>！</c> e <c>？</c> só se o nome for feito só de sinais), colchete nem marcador, para que uma
/// narração como <c>彼は言った。「やあ」</c> não vire falante.
/// </summary>
internal static class SpeakerSplitter
{
    public static bool TrySplit(string text, out SpeakerSplit split)
    {
        split = default;
        if (text.Length == 0 || !IsNameChar(text[0]))
        {
            return false;
        }

        for (var i = 1; i < text.Length; i++)
        {
            if (TryMatchAt(text, i, out split))
            {
                if (IsPlausibleName(split.Name))
                {
                    return true;
                }

                break;
            }

            if (!IsNameChar(text[i]))
            {
                break;
            }
        }

        split = default;
        return false;
    }

    /// <summary>Verdadeiro para uma fala inteira entre <c>「 」</c> ou <c>『 』</c>, sem nome na frente.</summary>
    public static bool IsQuoted(string text) =>
        text.Length >= 3 && ((text[0] == '「' && text[^1] == '」') || (text[0] == '『' && text[^1] == '』'));

    private static bool TryMatchAt(string text, int i, out SpeakerSplit split)
    {
        split = default;
        if (text.Length - i < 3)
        {
            return false;
        }

        var name = text[..i];
        if ((text[i] is '「' or '『') && (text[^1] is '」' or '』'))
        {
            split = new SpeakerSplit(name, text[i].ToString(), text[^1].ToString(), text[(i + 1)..^1]);
            return true;
        }

        if (text[i] == '（' && text[^1] == '）')
        {
            split = new SpeakerSplit(name, string.Empty, string.Empty, text[i..]);
            return true;
        }

        return false;
    }

    /// <summary>Exclamação ou interrogação só vale se o nome é feito só de sinais (<c>？？？</c> para um falante desconhecido).</summary>
    private static bool IsPlausibleName(string name) =>
        name.AsSpan().IndexOfAny("！？!?") < 0 || name.AsSpan().IndexOfAnyExcept("！？!?…・ 　") < 0;

    private static bool IsNameChar(char c) => c is not ('。' or '、' or '「' or '」' or '『' or '』' or '（' or '）' or '\\' or '{');
}
