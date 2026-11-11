using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VNForge.App.Controls;

/// <summary>Os tamanhos do retrato (tokens <c>Vnf.PortraitSm</c>, <c>Md</c> e <c>Lg</c>): o pequeno é redondo e os outros têm o canto do <c>Vnf.RadiusLg</c>, como no quadro.</summary>
public enum PortraitSize
{
    /// <summary>28 px, redondo: grade "Quem fala", chips da bíblia e ficha do VNDB.</summary>
    Sm,

    /// <summary>56 px, canto arredondado.</summary>
    Md,

    /// <summary>64 px, canto arredondado: cabeçalho do inspetor.</summary>
    Lg,
}

/// <summary>
/// O retrato de um personagem. Mostra a imagem do VNDB (<see cref="VndbImageControl.Image"/>) cortada para caber, com o rosto no alto; sem imagem, ou com a privacidade
/// escondendo-a, mostra o busto desenhado do quadro na cor do <see cref="VndbImageControl.Seed"/>. <see cref="IsNarrator"/> troca o busto pelo livro da narração.
/// </summary>
public sealed class Portrait : VndbImageControl
{
    public static readonly StyledProperty<PortraitSize> SizeProperty = AvaloniaProperty.Register<Portrait, PortraitSize>(nameof(Size), PortraitSize.Sm);
    public static readonly StyledProperty<bool> IsNarratorProperty = AvaloniaProperty.Register<Portrait, bool>(nameof(IsNarrator));
    public static readonly StyledProperty<CharacterColor?> TintProperty = AvaloniaProperty.Register<Portrait, CharacterColor?>(nameof(Tint));
    public static readonly StyledProperty<double> FixedSideProperty = AvaloniaProperty.Register<Portrait, double>(nameof(FixedSide), double.NaN);

    private const double Artboard = 64;

    // O busto do quadro numa prancha de 64 x 64: corpo, rosto e cabelo.
    private static readonly Geometry Body = StreamGeometry.Parse("M6 64c2-17 13-23 26-23s24 6 26 23z");
    private static readonly Geometry Hair = StreamGeometry.Parse("M20 28c-1-11 6-17 13-17s12 5 11 15c-4-3-8-6-12-6s-8 3-12 8z");
    private static readonly IBrush Skin = new SolidColorBrush(Color.Parse("#f1d9c4"));
    private static readonly IBrush HairBrush = new SolidColorBrush(Color.Parse("#1d1d2a"));

    static Portrait()
    {
        AffectsRender<Portrait>(IsNarratorProperty, TintProperty);
        AffectsMeasure<Portrait>(SizeProperty, FixedSideProperty);
        AffectsRender<Portrait>(SizeProperty);
    }

    public PortraitSize Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>A narração não tem rosto: o quadro desenha um livro numa caixa com borda.</summary>
    public bool IsNarrator
    {
        get => GetValue(IsNarratorProperty);
        set => SetValue(IsNarratorProperty, value);
    }

    /// <summary>A cor do personagem: o busto desenhado leva o matiz dela, como o nome dele; sem cor, o matiz sai do <see cref="VndbImageControl.Seed"/>.</summary>
    public CharacterColor? Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    protected override double BlurThumbnail => 8;

    protected override string DefaultAutomationName => IsNarrator ? "Narração" : Seed is { Length: > 0 } name ? $"Retrato de {name}" : "Retrato";

    /// <summary>Um lado em px que vale no lugar do token do tamanho: a lista e a página de personagens passam de 64 px. Sem valor (NaN), vale o token.</summary>
    public double FixedSide
    {
        get => GetValue(FixedSideProperty);
        set => SetValue(FixedSideProperty, value);
    }

    /// <summary>O lado do retrato, do token do tamanho ou do <see cref="FixedSide"/>.</summary>
    public double Side => !double.IsNaN(FixedSide) ? FixedSide : Size switch
    {
        PortraitSize.Md => Token("Vnf.PortraitMd", 56),
        PortraitSize.Lg => Token("Vnf.PortraitLg", 64),
        _ => Token("Vnf.PortraitSm", 28),
    };

    protected override Size MeasureOverride(Size availableSize) => new(Side, Side);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0)
        {
            return;
        }

        var rect = new Rect(0, 0, side, side);
        var radius = Size == PortraitSize.Sm ? side / 2 : Radius("Vnf.RadiusLg", 16).TopLeft;
        var round = new RoundedRect(rect, radius);
        context.DrawRectangle(Brush("Vnf.BgInset", Brushes.Gray), null, round);
        using (context.PushClip(round))
        {
            if (IsNarrator)
            {
                DrawNarrator(context, rect);
            }
            else if (LoadedBitmap is { } image)
            {
                context.DrawImage(image, SourceRectFor(image.PixelSize, rect.Size, 0), rect);
            }
            else
            {
                DrawBust(context, rect);
            }
        }

        if (IsNarrator)
        {
            context.DrawRectangle(null, new Pen(Brush("Vnf.StrokeSubtle", Brushes.Gray), 1), new RoundedRect(rect.Deflate(0.5), Math.Max(0, radius - 0.5)));
        }
    }

    private void DrawBust(DrawingContext context, Rect rect)
    {
        var hue = Tint is { } tint ? tint.Light.ToHsl().H : HueOf(Seed);
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Hsl(hue, 0.60, 0.80), 0), new GradientStop(Hsl(hue, 0.55, 0.54), 1)],
        };
        context.DrawRectangle(gradient, null, rect);
        var scale = rect.Width / Artboard;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(new SolidColorBrush(Hsl(hue, 0.35, 0.28)), null, Body);
            context.DrawEllipse(Skin, null, new Point(32, 27), 11, 11);
            context.DrawGeometry(HairBrush, null, Hair);
        }
    }

    private void DrawNarrator(DrawingContext context, Rect rect)
    {
        if (!this.TryFindResource("Icon.Book", ActualThemeVariant, out var found) || found is not Geometry book)
        {
            return;
        }

        // O ícone é desenhado numa grade de 20; ocupa 16 px no pequeno e cresce com o retrato.
        var icon = Math.Round(rect.Width * (Size == PortraitSize.Sm ? 16.0 / 28 : 0.4));
        var scale = icon / 20;
        var offset = (rect.Width - icon) / 2;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset, offset)))
        {
            context.DrawGeometry(Brush("Vnf.TextSecondary", Brushes.Gray), null, book);
        }
    }
}
