using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using VNForge.Core.Qa;

namespace VNForge.App.Controls;

/// <summary>
/// A tradução de uma variante com o que difere da referência marcado: <c>DiffText.Segments</c> num <see cref="TextBlock"/> escreve cada trecho de
/// <see cref="TextDiff.Compare"/>. O que a variante tem a mais fica com fundo de aviso e os espaços desenhados (<c>·</c>); o que falta aparece entre
/// <c>⟨ ⟩</c> na cor do aviso, na posição em que a referência o tem.
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

        foreach (var segment in segments)
        {
            switch (segment.Kind)
            {
                case TextDiffKind.Same:
                    block.Inlines?.Add(new Run(segment.Text));
                    break;
                case TextDiffKind.Added:
                    var added = new Run(segment.Visible) { FontWeight = FontWeight.SemiBold };
                    added.Bind(TextElement.ForegroundProperty, new DynamicResourceExtension("Vnf.SignalWarningFg"));
                    added.Bind(Inline.BackgroundProperty, new DynamicResourceExtension("Vnf.SignalWarningBg"));
                    block.Inlines?.Add(added);
                    break;
                default:
                    var missing = new Run("⟨" + segment.Visible + "⟩") { FontStyle = FontStyle.Italic };
                    missing.Bind(TextElement.ForegroundProperty, new DynamicResourceExtension("Vnf.SignalWarningFg"));
                    block.Inlines?.Add(missing);
                    break;
            }
        }
    }
}
