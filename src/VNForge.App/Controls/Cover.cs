using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VNForge.App.Controls;

/// <summary>
/// A capa de um jogo do VNDB, na proporção 3:4 do quadro. A largura é a do <see cref="Control.Width"/> ou, sem ela, a do token <c>Vnf.CoverThumbWidth</c>; a altura sai da largura.
/// Sem imagem, ou com a privacidade escondendo-a, mostra o marcador do quadro: degradê na cor do <see cref="VndbImageControl.Seed"/> e a faixa escura no pé.
/// </summary>
public sealed class Cover : VndbImageControl
{
    /// <summary>A capa tem 3 de largura para 4 de altura.</summary>
    public const double AspectHeight = 4.0 / 3.0;

    private static readonly IBrush Strip = new SolidColorBrush(Color.Parse("#0e1211"), 0.55);

    public static readonly StyledProperty<bool> FillWidthProperty = AvaloniaProperty.Register<Cover, bool>(nameof(FillWidth));

    static Cover() => FillWidthProperty.Changed.AddClassHandler<Cover>((cover, _) => cover.InvalidateMeasure());

    /// <summary>Usa a largura que o pai dá (a capa da grade, que muda com a janela) no lugar do token da miniatura; a altura segue sendo 4/3 da largura.</summary>
    public bool FillWidth
    {
        get => GetValue(FillWidthProperty);
        set => SetValue(FillWidthProperty, value);
    }

    protected override string DefaultAutomationName => Seed is { Length: > 0 } name ? $"Capa de {name}" : "Capa";

    private double WidthToUse(double available) =>
        !double.IsNaN(Width) ? Width
        : FillWidth && !double.IsInfinity(available) && available > 0 ? available
        : !double.IsInfinity(available) && available > 0 && available < Token("Vnf.CoverThumbWidth", 44) ? available
        : Token("Vnf.CoverThumbWidth", 44);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = WidthToUse(availableSize.Width);
        return new Size(width, width * AspectHeight);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = new Rect(Bounds.Size);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var round = new RoundedRect(rect, Radius("Vnf.RadiusSm", 8).TopLeft);
        context.DrawRectangle(Brush("Vnf.BgInset", Brushes.Gray), null, round);
        using (context.PushClip(round))
        {
            if (LoadedBitmap is { } image)
            {
                context.DrawImage(image, SourceRectFor(image.PixelSize, rect.Size, 0.5), rect);
            }
            else
            {
                DrawMarker(context, rect);
            }
        }
    }

    private void DrawMarker(DrawingContext context, Rect rect)
    {
        var hue = HueOf(Seed);
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Hsl(hue, 0.45, 0.38), 0), new GradientStop(Hsl(hue, 0.50, 0.16), 1)],
        };
        context.DrawRectangle(gradient, null, rect);

        // A faixa escura do pé: 24 em 160 da altura da capa do quadro.
        var strip = rect.Height * 24 / 160;
        context.DrawRectangle(Strip, null, new Rect(0, rect.Height - strip, rect.Width, strip));
    }
}
