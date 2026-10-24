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
/// A largura é medida em colunas, com <see cref="FontFamily"/> monoespaçada genérica. A decisão D6 (#105, fonte real do jogo) está em aberto:
/// quando sair, trocar a fonte e <see cref="ColumnWidth"/> (que passa a medir em pixels) é tudo que muda aqui.
/// </remarks>
public sealed class TextboxPreview : Control
{
    private const double Padding = 12;
    private const double BoxGap = 10;
    private const double PlateGap = 4;
    private const double PlatePadding = 8;

    public static readonly StyledProperty<TextboxLayout?> LayoutProperty =
        AvaloniaProperty.Register<TextboxPreview, TextboxLayout?>(nameof(Layout));

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<TextboxPreview, FontFamily>(nameof(FontFamily), FontFamily.Default);

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

    /// <summary>Fonte da prévia; o ponto de troca para a fonte real do jogo (D6).</summary>
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

        return new Size(layout.Columns * columnWidth + (Padding * 2), Math.Max(0, height - BoxGap));
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
                if (line.Text.Length > 0)
                {
                    context.DrawText(Format(line.Text, typeface, line.Overflow ? overflow : ink), new Point(Padding, y + Padding + (n * lineHeight)));
                }
            }

            if (layout.MaxLines is { } max && box.Lines.Count > max)
            {
                var limit = y + Padding + (max * lineHeight);
                context.DrawLine(new Pen(overflow, 1, DashStyle.Dash), new Point(Padding / 2, limit), new Point(width - (Padding / 2), limit));
            }

            y += height + BoxGap;
        }
    }

    /// <summary>Largura de uma coluna e altura de uma linha. Com a fonte real do jogo (D6), a largura passa a vir da medida em pixels do perfil.</summary>
    private (double ColumnWidth, double LineHeight) Metrics()
    {
        var sample = Format("0", new Typeface(FontFamily), Brushes.Black);
        return (sample.Width, sample.Height);
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

    private FormattedText Format(string text, Typeface typeface, IBrush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, FontSize, brush);

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : fallback;
}
