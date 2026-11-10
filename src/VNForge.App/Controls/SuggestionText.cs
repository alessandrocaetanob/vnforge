using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace VNForge.App.Controls;

/// <summary>
/// O texto da sugestão do editor (#532) num <see cref="TextBlock"/>: <c>Current</c> é a tradução de hoje e <c>Proposed</c> a proposta. Com <c>ShowRemoved</c> (o inspetor) a tradução de hoje
/// aparece riscada e a proposta em verde, depois do começo que não mudou, como no quadro. Sem ele (o Modo revisão) só a proposta aparece, com marca verde nas palavras que mudaram.
/// As cores vêm dos estilos <c>Run.ins</c> e <c>Run.sug-del</c> de <c>AiSuggestion.axaml</c>.
/// </summary>
public static class SuggestionText
{
    public static readonly AttachedProperty<string?> CurrentProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Current", typeof(SuggestionText));

    public static readonly AttachedProperty<string?> ProposedProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Proposed", typeof(SuggestionText));

    public static readonly AttachedProperty<bool> ShowRemovedProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("ShowRemoved", typeof(SuggestionText));

    static SuggestionText()
    {
        CurrentProperty.Changed.AddClassHandler<TextBlock>((block, _) => Fill(block));
        ProposedProperty.Changed.AddClassHandler<TextBlock>((block, _) => Fill(block));
        ShowRemovedProperty.Changed.AddClassHandler<TextBlock>((block, _) => Fill(block));
    }

    public static string? GetCurrent(TextBlock block) => block.GetValue(CurrentProperty);

    public static void SetCurrent(TextBlock block, string? value) => block.SetValue(CurrentProperty, value);

    public static string? GetProposed(TextBlock block) => block.GetValue(ProposedProperty);

    public static void SetProposed(TextBlock block, string? value) => block.SetValue(ProposedProperty, value);

    public static bool GetShowRemoved(TextBlock block) => block.GetValue(ShowRemovedProperty);

    public static void SetShowRemoved(TextBlock block, bool value) => block.SetValue(ShowRemovedProperty, value);

    private static void Fill(TextBlock block)
    {
        block.Text = null;
        block.Inlines ??= [];
        block.Inlines.Clear();
        var current = GetCurrent(block);
        var proposed = GetProposed(block);
        if (string.IsNullOrEmpty(proposed))
        {
            return;
        }

        if (GetShowRemoved(block))
        {
            // Como no quadro: o começo que não mudou fica sem marca; o resto da tradução de hoje vem riscado e o da proposta, em verde.
            current ??= string.Empty;
            var prefix = CommonWordPrefix(current, proposed);
            if (prefix.Length > 0)
            {
                block.Inlines.Add(new Run(prefix));
            }

            var removed = current[prefix.Length..];
            var added = proposed[prefix.Length..];
            if (removed.Length > 0)
            {
                block.Inlines.Add(Styled(removed, "sug-del"));
            }

            if (removed.Length > 0 && added.Length > 0)
            {
                block.Inlines.Add(new Run(" "));
            }

            if (added.Length > 0)
            {
                block.Inlines.Add(Styled(added, "ins"));
            }

            return;
        }

        var first = true;
        foreach (var segment in WordDiff.Compute(current, proposed).Where(s => s.Kind != DiffKind.Delete))
        {
            if (!first)
            {
                block.Inlines.Add(new Run(" "));
            }

            first = false;
            block.Inlines.Add(segment.Kind == DiffKind.Insert ? Styled(segment.Text, "ins") : new Run(segment.Text));
        }
    }

    /// <summary>O começo igual dos dois textos, cortado numa fronteira de palavra: nunca no meio de uma delas.</summary>
    private static string CommonWordPrefix(string current, string proposed)
    {
        var n = 0;
        while (n < current.Length && n < proposed.Length && current[n] == proposed[n])
        {
            n++;
        }

        for (; n > 0; n--)
        {
            var atWord = char.IsWhiteSpace(current[n - 1])
                || ((n == current.Length || char.IsWhiteSpace(current[n])) && (n == proposed.Length || char.IsWhiteSpace(proposed[n])));
            if (atWord)
            {
                break;
            }
        }

        return current[..n];
    }

    private static Run Styled(string text, string styleClass)
    {
        var run = new Run(text);
        run.Classes.Add(styleClass);
        return run;
    }
}
