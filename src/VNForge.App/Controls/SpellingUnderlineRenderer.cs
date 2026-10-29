using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using VNForge.Core.Qa;

namespace VNForge.App.Controls;

/// <summary>
/// Sublinhado ondulado em vermelho sob as palavras que o corretor não reconhece no campo da tradução. Desenha atrás do texto, na camada da seleção, então não
/// muda a medida nem a posição de nada. A palavra em que o cursor está não é sublinhada: ela ainda está sendo digitada.
/// </summary>
public sealed class SpellingUnderlineRenderer : IBackgroundRenderer
{
    private const double Step = 3;
    private const double Amplitude = 1.5;

    private IReadOnlyList<QaSpan> spans = [];

    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>Os trechos a sublinhar, no índice do texto do campo.</summary>
    public IReadOnlyList<QaSpan> Spans
    {
        get => spans;
        set => spans = value ?? [];
    }

    /// <summary>O offset do cursor enquanto o campo tem o foco; <c>null</c> sublinha tudo.</summary>
    public int? Caret { get; set; }

    /// <summary>Os trechos que o campo desenha: os de <see cref="Spans"/> menos o da palavra em que está o cursor.</summary>
    public IEnumerable<QaSpan> Visible => spans.Where(span => Caret is not { } offset || offset <= span.Start || offset > span.Start + span.Length);

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || textView.Document is not { } document)
        {
            return;
        }

        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), 1);
        foreach (var span in Visible)
        {
            if (span.Start < 0 || span.Start + span.Length > document.TextLength)
            {
                continue;
            }

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = span.Start, Length = span.Length }))
            {
                drawingContext.DrawGeometry(null, pen, Squiggle(rect.Left, rect.Right, rect.Bottom - Amplitude));
            }
        }
    }

    private static StreamGeometry Squiggle(double left, double right, double baseline)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(new Point(left, baseline), false);
        var up = false;
        for (var x = left + Step; x < right + Step; x += Step)
        {
            context.LineTo(new Point(Math.Min(x, right), baseline + (up ? 0 : Amplitude)));
            up = !up;
        }

        context.EndFigure(false);
        return geometry;
    }
}
