using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using VNForge.Core.Formatting;
using VNForge.Core.Qa;

namespace VNForge.App.Controls;

/// <summary>
/// A tradução de uma variante com o que difere da referência marcado: <c>DiffText.Segments</c> num <see cref="TextBlock"/> escreve cada trecho de
/// <see cref="TextDiff.Compare"/>. O que a variante tem a mais fica com fundo de aviso e os espaços desenhados (<c>·</c>); o que falta aparece entre
/// <c>⟨ ⟩</c> na cor do aviso, na posição em que a referência o tem. Os marcadores do jogo viram selo, como em <see cref="MarkedText"/>: o que a variante
/// tem a mais fica sobre o fundo de aviso e o que falta, entre <c>⟨ ⟩</c>, no selo de erro.
/// </summary>
public static class DiffText
{
    public static readonly AttachedProperty<IReadOnlyList<TextDiffSegment>?> SegmentsProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IReadOnlyList<TextDiffSegment>?>("Segments", typeof(DiffText));

    static DiffText() => SegmentsProperty.Changed.AddClassHandler<TextBlock>((block, e) => Fill(block, e.NewValue as IReadOnlyList<TextDiffSegment>));

    public static IReadOnlyList<TextDiffSegment>? GetSegments(TextBlock block) => block.GetValue(SegmentsProperty);

    public static void SetSegments(TextBlock block, IReadOnlyList<TextDiffSegment>? value) => block.SetValue(SegmentsProperty, value);

    private static void Fill(TextBlock block, IReadOnlyList<TextDiffSegment>? segments)
    {
        block.Text = null;
        block.Inlines?.Clear();
        if (segments is null)
        {
            return;
        }

        if (block.Inlines is not { } inlines)
        {
            return;
        }

        foreach (var segment in segments)
        {
            // O marcador do jogo é uma ficha só na comparação, então nenhum trecho o corta: cada um vira selo (MarkedText), como no resto das telas.
            var pieces = MarkedText.Split(segment.Text);
            switch (segment.Kind)
            {
                case TextDiffKind.Same:
                    AddPieces(inlines, pieces, marker => MarkedText.Chip(marker), text => new Run(text));
                    break;
                case TextDiffKind.Added:
                    AddPieces(inlines, pieces, AddedChip, AddedRun);
                    break;
                default:
                    inlines.Add(MissingRun("⟨"));
                    AddPieces(inlines, pieces, marker => MarkedText.Embed(MarkedText.Create(marker, isMissing: true)), text => MissingRun(TextDiff.Visible(text)));
                    inlines.Add(MissingRun("⟩"));
                    break;
            }
        }
    }

    private static void AddPieces(InlineCollection inlines, IReadOnlyList<TextSegment> pieces, Func<string, Inline> chip, Func<string, Inline> run)
    {
        foreach (var piece in pieces)
        {
            inlines.Add(piece.IsMarker ? chip(piece.Text) : run(piece.Text));
        }
    }

    private static Run AddedRun(string text)
    {
        var added = new Run(TextDiff.Visible(text)) { FontWeight = FontWeight.SemiBold };
        added.Bind(TextElement.ForegroundProperty, new DynamicResourceExtension("Vnf.SignalWarningFg"));
        added.Bind(Inline.BackgroundProperty, new DynamicResourceExtension("Vnf.SignalWarningBg"));
        return added;
    }

    /// <summary>O selo de um marcador a mais, sobre o fundo de aviso que o texto acrescentado tem.</summary>
    private static InlineUIContainer AddedChip(string marker)
    {
        var frame = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(2, 0), Child = MarkedText.Create(marker, isMissing: false) };
        frame.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Vnf.SignalWarningBg"));
        return MarkedText.Embed(frame);
    }

    private static Run MissingRun(string text)
    {
        var missing = new Run(text) { FontStyle = FontStyle.Italic };
        missing.Bind(TextElement.ForegroundProperty, new DynamicResourceExtension("Vnf.SignalWarningFg"));
        return missing;
    }
}
