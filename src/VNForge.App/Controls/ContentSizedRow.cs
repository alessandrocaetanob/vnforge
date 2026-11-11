using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>
/// Uma linha em que cada filho tem a largura do próprio conteúdo e a sobra da largura recebida é repartida em partes iguais entre eles (o flex "1 1 auto" do quadro).
/// Quando o conteúdo não cabe, todos encolhem na mesma proporção. É o painel do passo a passo do assistente, onde as legendas variam de 36 a 152 px.
/// </summary>
public sealed class ContentSizedRow : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }

        var natural = Children.Sum(c => c.DesiredSize.Width);
        var extra = (finalSize.Width - natural) / Children.Count;
        var scale = natural > finalSize.Width && natural > 0 ? finalSize.Width / natural : 1;
        double x = 0;
        foreach (var child in Children)
        {
            var width = scale < 1 ? child.DesiredSize.Width * scale : child.DesiredSize.Width + extra;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
