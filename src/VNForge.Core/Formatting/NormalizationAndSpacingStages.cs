using System.Text;
using System.Text.RegularExpressions;

namespace VNForge.Core.Formatting;

/// <summary>Unicode NFC: letra e acento soltos viram o caractere composto (E4.2).</summary>
internal sealed class NfcStage : TextRunStage
{
    public override string Id => "nfc";

    public override FormattingPhase Phase => FormattingPhase.Normalization;

    protected override string ApplyToRun(string run, FormattingContext context) => run.Normalize(NormalizationForm.FormC);
}

/// <summary>Remove os espaços de largura zero: U+200B, U+200C, U+200D, U+2060 e U+FEFF (E4.2).</summary>
internal sealed class InvisibleSpacesStage : TextRunStage
{
    private static readonly char[] Invisible = ['​', '‌', '‍', '⁠', '﻿'];

    public override string Id => "invisible-spaces";

    public override FormattingPhase Phase => FormattingPhase.Normalization;

    protected override string ApplyToRun(string run, FormattingContext context) =>
        run.IndexOfAny(Invisible) < 0 ? run : string.Concat(run.Split(Invisible));
}

/// <summary>Troca o espaço sem quebra (U+00A0) e o estreito sem quebra (U+202F) por espaço comum (E4.2).</summary>
internal sealed class NbspStage : TextRunStage
{
    public override string Id => "nbsp";

    public override FormattingPhase Phase => FormattingPhase.Normalization;

    protected override string ApplyToRun(string run, FormattingContext context) => run.Replace(' ', ' ').Replace(' ', ' ');
}

/// <summary>Colapsa dois ou mais espaços comuns em um (E4.2).</summary>
internal sealed partial class CollapseSpacesStage : TextRunStage
{
    public override string Id => "collapse-spaces";

    public override FormattingPhase Phase => FormattingPhase.Spacing;

    protected override string ApplyToRun(string run, FormattingContext context) => Repeated().Replace(run, " ");

    [GeneratedRegex(" {2,}")]
    private static partial Regex Repeated();
}

/// <summary>Tira o espaço antes de <c>? ! : ;</c> (E4.2).</summary>
internal sealed partial class SpaceBeforePunctuationStage : TextRunStage
{
    public override string Id => "space-before-punctuation";

    public override FormattingPhase Phase => FormattingPhase.Spacing;

    protected override string ApplyToRun(string run, FormattingContext context) => Before().Replace(run, "$1");

    [GeneratedRegex(" +([?!:;])")]
    private static partial Regex Before();
}

/// <summary>
/// Garante espaço depois da pontuação colada à palavra seguinte (E4.2): <c>, ; ? !</c> antes de uma letra, <c>.</c> e <c>:</c> entre uma
/// letra e uma maiúscula, e o fim de reticências antes de uma letra. Números (<c>1,5</c>, <c>10:30</c>) e endereços (<c>site.com</c>) ficam como estão.
/// </summary>
internal sealed partial class SpaceAfterPunctuationStage : TextRunStage
{
    public override string Id => "space-after-punctuation";

    public override FormattingPhase Phase => FormattingPhase.Spacing;

    protected override string ApplyToRun(string run, FormattingContext context)
    {
        run = Always().Replace(run, "$1 ");
        run = BeforeCapital().Replace(run, "$1 ");
        return AfterEllipsis().Replace(run, "$1 ");
    }

    [GeneratedRegex(@"([,;?!])(?=\p{L})")]
    private static partial Regex Always();

    [GeneratedRegex(@"(?<=\p{L})([.:])(?=\p{Lu})")]
    private static partial Regex BeforeCapital();

    [GeneratedRegex(@"(\.\.\.)(?=\p{L})")]
    private static partial Regex AfterEllipsis();
}
