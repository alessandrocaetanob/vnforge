using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VNForge.App.ViewModels;

namespace VNForge.App.Controls;

/// <summary>Tamanho da caixa do jogo: <c>Small</c> no cartão de acentos, <c>Medium</c> no editor e <c>Large</c> no modo revisão.</summary>
public enum GameTextboxSize
{
    Medium,
    Small,
    Large,
}

/// <summary>O que uma linha da grade da caixa mostra.</summary>
public enum GameTextboxRowKind
{
    /// <summary>Linha com texto dentro do limite de linhas.</summary>
    Text,

    /// <summary>Linha que o perfil permite e a tradução não usa: só o pontilhado.</summary>
    Vacant,

    /// <summary>Linha que passa do limite: fica abaixo de um traço.</summary>
    Overflow,

    /// <summary>Separador "página N" entre duas caixas do jogo.</summary>
    PageBreak,
}

/// <summary>Uma linha da grade da caixa. <paramref name="Line"/> é o índice da linha na página; nulo na linha vaga e no separador.</summary>
public sealed record GameTextboxRow(GameTextboxRowKind Kind, int Page, int? Line = null, bool FirstOverflow = false);

/// <summary>Onde uma linha da grade fica na caixa, em pixels a partir do canto do controle. <paramref name="GapAbove"/> é o espaço do traço de overflow acima da linha.</summary>
public sealed record GameTextboxRowBounds(GameTextboxRow Row, Rect Bounds, double GapAbove);

/// <summary>
/// A caixa de texto do jogo (docs/design/components/TextboxPreview): placa do nome, as linhas que o perfil permite com as vagas pontilhadas,
/// o que passa do limite abaixo de um traço, o triângulo onde o jogo espera o clique e, com mais de uma caixa, o separador "página N".
/// Só desenha o <see cref="Layout"/>; as quebras vêm do Core (<see cref="TextboxPreviewViewModel"/>). As cores são os tokens <c>textbox-*</c>.
/// </summary>
/// <remarks>
/// A largura é medida em colunas. A fonte é a que o jogo pede, MS Gothic (ＭＳ ゴシック, largura fixa: latim em meia largura, japonês em largura inteira),
/// ou, sem ela instalada (Linux, CI, Windows sem o pacote japonês), a BIZ UDGothic embutida, de métricas equivalentes (D6, #105).
/// O triângulo oscila sob a classe <c>motion</c> da janela (Themes/Controls/Motion.axaml); sem ela, o Windows pediu menos movimento e ele fica parado.
/// </remarks>
public sealed class GameTextbox : Control
{
    private const double MarkerScale = 0.6;
    private const double MarkerOpacity = 0.55;
    private const double OverflowGap = 8;
    private const double PageTail = 16;
    private const double PageSeparatorHeight = 30;
    private const double NextWidth = 12;
    private const double NextHeight = 9;

    public static readonly StyledProperty<TextboxLayout?> LayoutProperty =
        AvaloniaProperty.Register<GameTextbox, TextboxLayout?>(nameof(Layout));

    /// <summary>A MS Gothic do sistema primeiro e, na falta dela, a BIZ UDGothic embutida (SIL OFL 1.1; a fonte da Microsoft não pode ser redistribuída).</summary>
    public static readonly FontFamily GameFont = new("MS Gothic, avares://VNForge.App/Assets/Fonts#BIZ UDGothic");

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<GameTextbox, FontFamily>(nameof(FontFamily), GameFont);

    public static readonly StyledProperty<GameTextboxSize> SizeProperty =
        AvaloniaProperty.Register<GameTextbox, GameTextboxSize>(nameof(Size));

    /// <summary>Deslocamento do triângulo de virar a página, em pixels. Só a animação de <c>Motion.axaml</c> o move.</summary>
    public static readonly StyledProperty<double> NextOffsetProperty =
        AvaloniaProperty.Register<GameTextbox, double>(nameof(NextOffset));

    static GameTextbox()
    {
        AffectsMeasure<GameTextbox>(LayoutProperty, FontFamilyProperty, SizeProperty);
        AffectsRender<GameTextbox>(LayoutProperty, FontFamilyProperty, SizeProperty, NextOffsetProperty);
    }

    public GameTextbox() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

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

    public GameTextboxSize Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double NextOffset
    {
        get => GetValue(NextOffsetProperty);
        set => SetValue(NextOffsetProperty, value);
    }

    /// <summary>A grade de linhas que o controle desenha para o <see cref="Layout"/> atual; vazia sem ele.</summary>
    public IReadOnlyList<GameTextboxRow> Rows => Layout is { } layout ? Describe(layout) : [];

    /// <summary>
    /// Monta a grade: cada página tem as linhas do perfil (ou as que a tradução usa, se forem mais). A linha com texto dentro do limite é <c>Text</c>,
    /// a que o perfil permite e ficou sem texto é <c>Vacant</c> e a que passa do limite é <c>Overflow</c>. Entre páginas entra um <c>PageBreak</c>.
    /// </summary>
    public static IReadOnlyList<GameTextboxRow> Describe(TextboxLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var rows = new List<GameTextboxRow>();
        for (var page = 0; page < layout.Boxes.Count; page++)
        {
            if (page > 0)
            {
                rows.Add(new GameTextboxRow(GameTextboxRowKind.PageBreak, page));
            }

            var lines = layout.Boxes[page].Lines;
            for (var n = 0; n < Math.Max(lines.Count, layout.MaxLines ?? 1); n++)
            {
                if (n >= lines.Count)
                {
                    rows.Add(new GameTextboxRow(GameTextboxRowKind.Vacant, page, null));
                }
                else if (lines[n].Overflow || (layout.MaxLines is { } max && n >= max))
                {
                    rows.Add(new GameTextboxRow(GameTextboxRowKind.Overflow, page, n, FirstOverflow: n == layout.MaxLines));
                }
                else
                {
                    rows.Add(new GameTextboxRow(GameTextboxRowKind.Text, page, n));
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// O retângulo de cada linha da grade, na mesma conta do desenho: o miolo do texto, sem a placa nem as bordas. Serve para conferir o que foi pintado
    /// em cada linha sem depender de como a fonte desenha os glifos.
    /// </summary>
    public IReadOnlyList<GameTextboxRowBounds> RowBounds()
    {
        if (Layout is not { } layout)
        {
            return [];
        }

        var m = Metrics.For(Size);
        var (columnWidth, textHeight) = Cell(m);
        var rowHeight = Math.Max(m.RowHeight, textHeight);
        var y = (layout.Speaker is null ? 0 : m.PlateOverhang) + m.PadTop;
        var bounds = new List<GameTextboxRowBounds>();
        foreach (var row in Describe(layout))
        {
            var gap = row.FirstOverflow ? OverflowGap : 0;
            var height = row.Kind == GameTextboxRowKind.PageBreak ? PageTail + PageSeparatorHeight : rowHeight;
            bounds.Add(new GameTextboxRowBounds(row, new Rect(m.PadX, y + gap, layout.Columns * columnWidth, height), gap));
            y += gap + height;
        }

        return bounds;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Layout is not { } layout)
        {
            return default;
        }

        var m = Metrics.For(Size);
        var (columnWidth, textHeight) = Cell(m);
        var rowHeight = Math.Max(m.RowHeight, textHeight);
        var height = HeightOf(layout, m, rowHeight);
        return new Size(ContentWidth(layout, m, columnWidth), height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Layout is not { } layout)
        {
            return;
        }

        var m = Metrics.For(Size);
        var (columnWidth, textHeight) = Cell(m);
        var rowHeight = Math.Max(m.RowHeight, textHeight);
        var ink = Brush("Vnf.TextboxInk", Brushes.White);
        var paint = new Paint(
            m,
            columnWidth,
            textHeight,
            rowHeight,
            (layout.Columns * columnWidth) + (m.PadX * 2),
            new Typeface(FontFamily),
            ink,
            Brush("Vnf.TextboxOverflow", Brushes.Salmon),
            new DashStyle([1, 2], 0),
            new DashStyle([4, 3], 0));

        var top = layout.Speaker is null ? 0 : m.PlateOverhang;
        var frame = new Rect(0, top, paint.Width, HeightOf(layout, m, rowHeight) - top);
        context.DrawRectangle(Brush("Vnf.TextboxBg", Brushes.Black), new Pen(Tint(ink, 0.16), 1), new RoundedRect(frame.Deflate(0.5), 12));
        context.DrawRectangle(null, new Pen(Tint(ink, 0.07), 1), new RoundedRect(frame.Deflate(5.5), 8));

        var y = top + m.PadTop;
        foreach (var row in Describe(layout))
        {
            y = DrawRow(context, layout, row, y, paint);
        }

        DrawNext(context, ink, paint.Width, y + m.NextTop, m);
        if (layout.Speaker is { } speaker)
        {
            DrawPlate(context, speaker, paint.Typeface, ink, m);
        }
    }

    /// <summary>Desenha uma linha da grade em <paramref name="y"/> e devolve onde começa a próxima.</summary>
    private double DrawRow(DrawingContext context, TextboxLayout layout, GameTextboxRow row, double y, Paint p)
    {
        var m = p.M;
        if (row.Kind == GameTextboxRowKind.PageBreak)
        {
            DrawNext(context, p.Ink, p.Width, y + m.NextTop, m);
            DrawPageBreak(context, row.Page, y + PageTail, p);
            return y + PageTail + PageSeparatorHeight;
        }

        if (row.FirstOverflow)
        {
            var line = y + (OverflowGap / 2);
            context.DrawLine(new Pen(p.Overflow, 1, p.Dashed), new Point(m.PadX - 6, line), new Point(p.Width - m.PadX + 6, line));
            y += OverflowGap;
        }

        var isOverflow = row.Kind == GameTextboxRowKind.Overflow;
        if (row.Line is { } n)
        {
            DrawLine(context, layout.Boxes[row.Page].Lines[n], p.Typeface, isOverflow ? p.Overflow : p.Ink, p.ColumnWidth, new Point(m.PadX, y + ((p.RowHeight - p.TextHeight) / 2)), m);
        }

        var rule = y + p.RowHeight - 0.5;
        context.DrawLine(new Pen(Tint(isOverflow ? p.Overflow : p.Ink, isOverflow ? 0.35 : 0.13), 1, p.Dotted), new Point(m.PadX, rule), new Point(m.PadX + (layout.Columns * p.ColumnWidth), rule));
        return y + p.RowHeight;
    }

    private static double HeightOf(TextboxLayout layout, Metrics m, double rowHeight)
    {
        var height = (layout.Speaker is null ? 0 : m.PlateOverhang) + m.PadTop + m.PadBottom;
        foreach (var row in Describe(layout))
        {
            height += row.Kind == GameTextboxRowKind.PageBreak ? PageTail + PageSeparatorHeight : rowHeight;
            height += row.FirstOverflow ? OverflowGap : 0;
        }

        return height;
    }

    /// <summary>A caixa mede as colunas do perfil; a placa de um nome mais largo que elas estica o controle em vez de ser cortada.</summary>
    private double ContentWidth(TextboxLayout layout, Metrics m, double columnWidth)
    {
        var box = (layout.Columns * columnWidth) + (m.PadX * 2);
        return layout.Speaker is { } speaker ? Math.Max(box, m.PlateLeft + Format(speaker, new Typeface(FontFamily), Brushes.Black, m.PlateFont).Width + (m.PlatePadX * 2) + 8) : box;
    }

    /// <summary>
    /// Largura de uma coluna (a meia largura do latim) e altura de um texto, a maior entre o latino e um glifo japonês.
    /// </summary>
    private (double ColumnWidth, double TextHeight) Cell(Metrics m)
    {
        var typeface = new Typeface(FontFamily);
        var latin = Format("0", typeface, Brushes.Black, m.FontSize);
        return (latin.Width, Math.Max(latin.Height, Format("あ", typeface, Brushes.Black, m.FontSize).Height));
    }

    /// <summary>Cada trecho vai para a coluna dele na régua do perfil, não para onde a fonte o poria: glifo largo ocupa as colunas da tabela (encolhido se a fonte o fizer maior) e marcador não avança a coluna.</summary>
    private static void DrawLine(DrawingContext context, TextboxLine line, Typeface typeface, IBrush ink, double columnWidth, Point origin, Metrics m)
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
                context.DrawText(Format(segment.Text, typeface, ink, m.FontSize * MarkerScale), at);
            }
            else
            {
                DrawGlyph(context, Format(segment.Text, typeface, ink, m.FontSize), segment.Width * columnWidth, at);
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

    private void DrawPlate(DrawingContext context, string speaker, Typeface typeface, IBrush ink, Metrics m)
    {
        var text = Format(speaker, typeface, ink, m.PlateFont);
        var plate = new Rect(m.PlateLeft, 0, text.Width + (m.PlatePadX * 2), m.PlateHeight);
        context.DrawRectangle(new SolidColorBrush(Colors.Black, 0.35), null, new RoundedRect(plate.Translate(new Vector(0, 2)), 6));
        context.DrawRectangle(Brush("Vnf.TextboxPlate", Brushes.SteelBlue), null, new RoundedRect(plate, 6));
        context.DrawText(text, new Point(plate.X + m.PlatePadX, (m.PlateHeight - text.Height) / 2));
    }

    /// <summary>O triângulo onde o jogo espera o clique, na ponta direita da página.</summary>
    private void DrawNext(DrawingContext context, IBrush ink, double width, double y, Metrics m)
    {
        var x = width - m.NextInset - NextWidth;
        var top = y + NextOffset;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(x, top), true);
            g.LineTo(new Point(x + NextWidth, top));
            g.LineTo(new Point(x + (NextWidth / 2), top + NextHeight));
            g.EndFigure(true);
        }

        using var _ = context.PushOpacity(0.8);
        context.DrawGeometry(ink, null, geometry);
    }

    /// <summary>"página N" e um traço tracejado até a borda direita.</summary>
    private static void DrawPageBreak(DrawingContext context, int page, double y, Paint p)
    {
        var label = Format($"página {page + 1}", p.Typeface, Tint(p.Ink, 0.55), 10);
        context.DrawText(label, new Point(p.M.PadX, y + 10));
        var line = y + 10 + (label.Height / 2);
        context.DrawLine(new Pen(Tint(p.Ink, 0.22), 1, p.Dashed), new Point(p.M.PadX + label.Width + 8, line), new Point(p.Width - p.M.PadX, line));
    }

    private static FormattedText Format(string text, Typeface typeface, IBrush brush, double size) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush);

    private static IBrush Tint(IBrush brush, double opacity) =>
        brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, opacity) : brush;

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : fallback;

    /// <summary>O que o desenho de uma passada precisa: medidas, fonte, tintas e traços.</summary>
    private readonly record struct Paint(
        Metrics M,
        double ColumnWidth,
        double TextHeight,
        double RowHeight,
        double Width,
        Typeface Typeface,
        IBrush Ink,
        IBrush Overflow,
        DashStyle Dotted,
        DashStyle Dashed);

    /// <summary>As medidas do desenho da caixa em cada tamanho (v3.css, <c>.gbox</c>, <c>.gbox.sm</c> e <c>.gbox.lg</c>).</summary>
    private readonly record struct Metrics(
        double PadTop,
        double PadX,
        double PadBottom,
        double RowHeight,
        double FontSize,
        double PlateHeight,
        double PlateOverhang,
        double PlateLeft,
        double PlatePadX,
        double PlateFont,
        double NextInset,
        double NextTop)
    {
        public static Metrics For(GameTextboxSize size) => size switch
        {
            GameTextboxSize.Small => new(20, 16, 24, 22, 12, 24, 12, 14, 12, 12, 12, 5),
            GameTextboxSize.Large => new(30, 32, 34, 32, 17, 28, 14, 28, 14, 13, 20, 11),
            _ => new(24, 26, 28, 26, 14, 24, 12, 22, 12, 12, 20, 5),
        };
    }
}
