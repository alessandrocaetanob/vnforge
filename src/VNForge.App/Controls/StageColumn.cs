using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>
/// A coluna do palco: o primeiro filho é o <see cref="ReviewStage"/> e os demais ficam sob ele, empilhados. Os de baixo são medidos primeiro e o palco leva
/// a altura que sobra, então ele encolhe quando um aviso aparece em vez de empurrar a ação para fora da tela.
/// </summary>
public sealed class StageColumn : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<StageColumn, double>(nameof(Spacing), 12);

    /// <summary>O espaço entre o palco e o que vem abaixo, e entre os filhos de baixo.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    static StageColumn() => AffectsMeasure<StageColumn>(SpacingProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            return default;
        }

        var stage = Children[0];
        var width = availableSize.Width;
        var below = MeasureBelow(width);
        stage.Measure(StageSpace(availableSize, below, width));

        // Com o palco mais estreito que a coluna, o texto de baixo quebra mais: mede de novo na largura dele e dá ao palco o que sobrar.
        if (stage.DesiredSize.Width < width)
        {
            width = stage.DesiredSize.Width;
            below = MeasureBelow(width);
            stage.Measure(StageSpace(availableSize, below, width));
        }

        return new Size(stage.DesiredSize.Width, stage.DesiredSize.Height + below);
    }

    private double MeasureBelow(double width)
    {
        var below = 0d;
        for (var i = 1; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(width, double.PositiveInfinity));
            if (Children[i].IsVisible)
            {
                below += Children[i].DesiredSize.Height + Spacing;
            }
        }

        return below;
    }

    private static Size StageSpace(Size available, double below, double width) =>
        new(width, double.IsInfinity(available.Height) ? double.PositiveInfinity : Math.Max(0, available.Height - below));

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0d;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (!child.IsVisible)
            {
                continue;
            }

            var width = i == 0 ? child.DesiredSize.Width : finalSize.Width;
            if (i > 0)
            {
                child.Measure(new Size(width, double.PositiveInfinity));
            }

            child.Arrange(new Rect(0, y, width, child.DesiredSize.Height));
            y += child.DesiredSize.Height + Spacing;
        }

        return finalSize;
    }
}
