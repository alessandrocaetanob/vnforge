using System.Text;

namespace VNForge.Core.Formatting;

/// <summary>Aspas curvas para retas ou para <c>「 」</c> (E4.3).</summary>
internal sealed class QuotesStage(QuoteStyle style) : TextRunStage
{
    public override string Id => "quotes";

    public override FormattingPhase Phase => FormattingPhase.Typography;

    protected override string ApplyToRun(string run, FormattingContext context) => style switch
    {
        QuoteStyle.Ascii => run.Replace('“', '"').Replace('”', '"').Replace('‘', '\'').Replace('’', '\''),
        QuoteStyle.Corner => run.Replace('“', '「').Replace('”', '」'),
        _ => run,
    };
}

/// <summary><c>...</c> e <c>…</c> nos dois sentidos (E4.3).</summary>
internal sealed class EllipsisStage(EllipsisStyle style) : TextRunStage
{
    public override string Id => "ellipsis";

    public override FormattingPhase Phase => FormattingPhase.Typography;

    protected override string ApplyToRun(string run, FormattingContext context) => style switch
    {
        EllipsisStyle.Character => run.Replace("...", "…", StringComparison.Ordinal),
        EllipsisStyle.Dots => run.Replace("…", "...", StringComparison.Ordinal),
        _ => run,
    };
}

/// <summary>Troca o travessão <c>—</c> e <c>–</c> pelo caractere do perfil, em qualquer posição (E4.3).</summary>
internal sealed class DashStage(char dash) : TextRunStage
{
    public override string Id => "dash";

    public override FormattingPhase Phase => FormattingPhase.Typography;

    protected override string ApplyToRun(string run, FormattingContext context) => run.Replace('—', dash).Replace('–', dash);
}

/// <summary>
/// A fala que abre com travessão sai com o caractere do perfil (E4.3). O começo da linha é o começo do texto e o que vem depois de
/// <c>\n</c> ou <c>\p</c>; marcadores de cor ou de variável antes do travessão não tiram a fala do começo da linha. O hífen só conta
/// quando vem seguido de espaço, para <c>-1</c> e <c>-x</c> não virarem fala.
/// </summary>
internal sealed class DialogueDashStage(char dash) : IFormattingStage
{
    public string Id => "dialogue-dash";

    public FormattingPhase Phase => FormattingPhase.Typography;

    public string Apply(string text, FormattingContext context)
    {
        var result = new StringBuilder(text.Length);
        var atLineStart = true;
        foreach (var segment in ControlMarkers.Split(text))
        {
            if (segment.IsMarker)
            {
                result.Append(segment.Text);
                atLineStart |= segment.Text is @"\n" or @"\p";
                continue;
            }

            var run = segment.Text;
            if (run.Length == 0)
            {
                continue;
            }

            if (atLineStart && IsOpeningDash(run))
            {
                result.Append(dash).Append(run, 1, run.Length - 1);
            }
            else
            {
                result.Append(run);
            }

            atLineStart = false;
        }

        return result.ToString();
    }

    private static bool IsOpeningDash(string run) =>
        run[0] is '—' or '–' or '―' || (run[0] == '-' && run.Length > 1 && run[1] == ' ');
}

/// <summary>ASCII visível entre meia largura e largura cheia (E4.3).</summary>
internal sealed class WidthStage(WidthRule rule) : TextRunStage
{
    private const int FullWidthOffset = 0xFF01 - 0x21;

    public override string Id => "width";

    public override FormattingPhase Phase => FormattingPhase.Width;

    protected override string ApplyToRun(string run, FormattingContext context)
    {
        var result = new StringBuilder(run.Length);
        foreach (var c in run)
        {
            result.Append(rule.Mode == WidthMode.Full ? ToFull(c) : ToHalf(c));
        }

        return result.ToString();
    }

    // Barra e chaves ficam: virar ou deixar de ser marcador muda o sentido do texto.
    private static bool IsProtected(char c) => c is '\\' or '{' or '}';

    private char ToFull(char c)
    {
        if (c == ' ')
        {
            return rule.Spaces ? '　' : c;
        }

        return c is > ' ' and <= '~' && !IsProtected(c) ? (char)(c + FullWidthOffset) : c;
    }

    private char ToHalf(char c)
    {
        if (c == '　')
        {
            return rule.Spaces ? ' ' : c;
        }

        if (c is >= '！' and <= '～')
        {
            var ascii = (char)(c - FullWidthOffset);
            return IsProtected(ascii) ? c : ascii;
        }

        return c;
    }
}
