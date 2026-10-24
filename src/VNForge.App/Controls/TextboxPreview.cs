using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VNForge.App.ViewModels;

namespace VNForge.App.Controls;

/// <summary>
/// Simulação da caixa de texto do jogo (docs/design/components/TextboxPreview): placa do falante, linhas já quebradas e o que passa do limite de linhas.
/// Só desenha o <see cref="Layout"/>; as quebras vêm do Core (<see cref="TextboxPreviewViewModel"/>). As cores são os tokens <c>textbox-*</c>.
/// </summary>
/// <remarks>
/// A largura é medida em colunas. A fonte é a que o jogo pede, MS Gothic (ＭＳ ゴシック, largura fixa: latim em meia largura, japonês em largura inteira),
/// ou, sem ela instalada (Linux, CI, Windows sem o pacote japonês), a BIZ UDGothic embutida, de métricas equivalentes (D6, #105).
/// </remarks>
public sealed class TextboxPreview : Control
{
    private const double Padding = 12;
    private const double BoxGap = 10;
    private const double PlateGap = 4;
    private const double PlatePadding = 8;
    private const double MarkerScale = 0.6;
    private const double MarkerOpacity = 0.55;

    public static readonly StyledProperty<TextboxLayout?> LayoutProperty =
        AvaloniaProperty.Register<TextboxPreview, TextboxLayout?>(nameof(Layout));

    /// <summary>A MS Gothic do sistema primeiro e, na falta dela, a BIZ UDGothic embutida (SIL OFL 1.1; a fonte da Microsoft não pode ser redistribuída).</summary>
    public static readonly FontFamily GameFont = new("MS Gothic, avares://VNForge.App/Assets/Fonts#BIZ UDGothic");

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<TextboxPreview, FontFamily>(nameof(FontFamily), GameFont);

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<TextboxPreview, double>(nameof(FontSize), 14);

    static TextboxPreview()
    {
        AffectsMeasure<TextboxPreview>(LayoutProperty, FontFamilyProperty, FontSizeProperty);
        AffectsRender<TextboxPreview>(LayoutProperty, FontFamilyProperty, FontSizeProperty);
    }

    public TextboxPreview() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public TextboxLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>Fonte da prévia; por padrão <see cref="GameFont"/>.</summary>
    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Layout is not { } layout)
        {
            return default;
        }

        var (columnWidth, lineHeight) = Metrics();
        var height = 0.0;
        foreach (var box in layout.Boxes)
        {
            height += BoxHeight(layout, box, lineHeight) + PlateHeight(layout, lineHeight) + BoxGap;
        }

        return new Size(ContentWidth(layout, columnWidth), Math.Max(0, height - BoxGap));
    }

    /// <summary>A caixa mede as colunas do perfil; a placa de um nome mais largo que elas estica o controle em vez de ser cortada.</summary>
    private double ContentWidth(TextboxLayout layout, double columnWidth)
    {
        var box = layout.Columns * columnWidth + (Padding * 2);
        return layout.Speaker is { } speaker ? Math.Max(box, Format(speaker, new Typeface(FontFamily), Brushes.Black).Width + (PlatePadding * 2)) : box;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Layout is not { } layout)
        {
            return;
        }

        var (columnWidth, lineHeight) = Metrics();
        var width = layout.Columns * columnWidth + (Padding * 2);
        var typeface = new Typeface(FontFamily);
        var ink = Brush("Vnf.TextboxInk", Brushes.White);
        var overflow = Brush("Vnf.TextboxOverflow", Brushes.Salmon);
        var y = 0.0;
        foreach (var box in layout.Boxes)
        {
            if (layout.Speaker is { } speaker)
            {
                y += DrawPlate(context, speaker, typeface, ink, y, lineHeight);
            }

            var height = BoxHeight(layout, box, lineHeight);
            context.DrawRectangle(Brush("Vnf.TextboxBg", Brushes.Black), null, new RoundedRect(new Rect(0, y, width, height), 6));
            for (var n = 0; n < box.Lines.Count; n++)
            {
                var line = box.Lines[n];
                DrawLine(context, line, typeface, line.Overflow ? overflow : ink, columnWidth, new Point(Padding, y + Padding + (n * lineHeight)));
            }

            if (layout.MaxLines is { } max && box.Lines.Count > max)
            {
                var limit = y + Padding + (max * lineHeight);
                context.DrawLine(new Pen(overflow, 1, DashStyle.Dash), new Point(Padding / 2, limit), new Point(width - (Padding / 2), limit));
            }

            y += height + BoxGap;
        }
    }

    /// <summary>
    /// Largura de uma coluna (a meia largura do latim) e altura de uma linha, a maior entre o latino e um glifo japonês.
    /// </summary>
    private (double ColumnWidth, double LineHeight) Metrics()
    {
        var typeface = new Typeface(FontFamily);
        var latin = Format("0", typeface, Brushes.Black);
        return (latin.Width, Math.Max(latin.Height, Format("あ", typeface, Brushes.Black).Height));
    }

    /// <summary>Cada trecho vai para a coluna dele na régua do perfil, não para onde a fonte o poria: glifo largo ocupa as colunas da tabela (encolhido se a fonte o fizer maior) e marcador não avança a coluna.</summary>
    private void DrawLine(DrawingContext context, TextboxLine line, Typeface typeface, IBrush ink, double columnWidth, Point origin)
    {
        foreach (var segment in line.Segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Text))
            {
                continue;
            }

            var at = new Point(origin.X + (segment.Column * columnWidth), origin.Y);
            if (segment.IsMarker)
            {
                using var _ = context.PushOpacity(MarkerOpacity);
                context.DrawText(Format(segment.Text, typeface, ink, FontSize * MarkerScale), at);
            }
            else
            {
                DrawGlyph(context, Format(segment.Text, typeface, ink), segment.Width * columnWidth, at);
            }
        }
    }

    /// <summary>Desenha o glifo em <paramref name="at"/>, encolhido na horizontal quando a fonte o faz mais largo que as colunas da tabela.</summary>
    private static void DrawGlyph(DrawingContext context, FormattedText glyph, double target, Point at)
    {
        if (target <= 0 || glyph.Width <= target + 0.5)
        {
            context.DrawText(glyph, at);
            return;
        }

        using var _ = context.PushTransform(Matrix.CreateScale(target / glyph.Width, 1) * Matrix.CreateTranslation(at.X, at.Y));
        context.DrawText(glyph, default);
    }

    private double DrawPlate(DrawingContext context, string speaker, Typeface typeface, IBrush ink, double y, double lineHeight)
    {
        var text = Format(speaker, typeface, ink);
        var plate = new Rect(0, y, text.Width + (PlatePadding * 2), lineHeight + PlatePadding);
        context.DrawRectangle(Brush("Vnf.TextboxPlate", Brushes.SteelBlue), null, new RoundedRect(plate, 4));
        context.DrawText(text, new Point(PlatePadding, y + (PlatePadding / 2)));
        return plate.Height + PlateGap;
    }

    private static double PlateHeight(TextboxLayout layout, double lineHeight) =>
        layout.Speaker is null ? 0 : lineHeight + PlatePadding + PlateGap;

    private static double BoxHeight(TextboxLayout layout, TextboxBox box, double lineHeight) =>
        (Math.Max(box.Lines.Count, layout.MaxLines ?? 1) * lineHeight) + (Padding * 2);

    private FormattedText Format(string text, Typeface typeface, IBrush brush, double? size = null) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size ?? FontSize, brush);

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : fallback;
}
