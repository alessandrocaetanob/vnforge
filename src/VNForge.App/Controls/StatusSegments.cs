using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VNForge.Core.Factsheet;

namespace VNForge.App.Controls;

/// <summary>
/// A barra do cartão "Texto em português" da Ficha: um segmento por etapa (aprovado, revisado, traduzido, IA e, se houver, sem tradução), com 2 px entre eles, 8 px de altura e os cantos
/// arredondados do conjunto. A largura de cada um é proporcional à contagem; sem nenhum texto a barra fica só com o trilho. As cores são os tokens <c>seg-*</c>.
/// </summary>
public sealed class StatusSegments : Control
{
    public static readonly StyledProperty<SheetStatusCounts?> CountsProperty = AvaloniaProperty.Register<StatusSegments, SheetStatusCounts?>(nameof(Counts));

    private const double Gap = 2;
    private static readonly string[] Keys = ["Vnf.SegApproved", "Vnf.SegReviewed", "Vnf.SegTranslated", "Vnf.SegAi", "Vnf.SegPending"];

    static StatusSegments() => AffectsRender<StatusSegments>(CountsProperty);

    public SheetStatusCounts? Counts
    {
        get => GetValue(CountsProperty);
        set => SetValue(CountsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, 8);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        if (Counts is not { Total: > 0 } counts || bounds.Width <= 0)
        {
            return;
        }

        int[] values = [counts.Approved, counts.Reviewed, counts.Translated, counts.Machine, counts.Pending];
        var used = values.Count(v => v > 0);
        var room = bounds.Width - (Gap * (used - 1));
        var x = 0.0;
        using var clip = context.PushClip(new RoundedRect(bounds, bounds.Height / 2));
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] == 0)
            {
                continue;
            }

            var width = room * values[i] / counts.Total;
            context.FillRectangle(Brush(Keys[i]), new Rect(x, 0, width, bounds.Height));
            x += width + Gap;
        }
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.Transparent;
}
