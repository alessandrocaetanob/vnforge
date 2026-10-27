using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>
/// A coluna do palco: o primeiro filho é o <see cref="ReviewStage"/> e os demais ficam sob ele, empilhados. Os de baixo são medidos primeiro e o palco leva
/// a altura que sobra, então ele encolhe quando um aviso aparece em vez de empurrar a ação para fora da tela. O palco nunca fica abaixo de
/// <see cref="MinStageHeight"/>: o que vem abaixo recebe só o resto da altura, e um <c>ScrollViewer</c> como filho rola quando não cabe.
/// </summary>
public sealed class StageColumn : Panel
{
    private const int MaxPasses = 4;

    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<StageColumn, double>(nameof(Spacing), 12);
    public static readonly StyledProperty<double> MinStageHeightProperty = AvaloniaProperty.Register<StageColumn, double>(nameof(MinStageHeight), 180);

    private double columnWidth;

    /// <summary>O espaço entre o palco e o que vem abaixo, e entre os filhos de baixo.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>A altura mínima do palco, mesmo com muitos avisos embaixo.</summary>
    public double MinStageHeight
    {
        get => GetValue(MinStageHeightProperty);
        set => SetValue(MinStageHeightProperty, value);
    }

    static StageColumn() => AffectsMeasure<StageColumn>(SpacingProperty, MinStageHeightProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            return default;
        }

        var stage = Children[0];
        var width = availableSize.Width;
        var maxBelow = double.IsInfinity(availableSize.Height) ? double.PositiveInfinity : Math.Max(0, availableSize.Height - MinStageHeight);

        // Um palco limitado pela altura fica mais estreito que a coluna, e o texto de baixo quebra mais nessa largura. Repete até a largura
        // do palco parar de mudar (cada passada só a diminui), para os filhos serem medidos na mesma largura em que serão organizados.
        var below = 0d;
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            below = MeasureBelow(width, maxBelow);
            stage.Measure(new Size(width, StageHeight(availableSize.Height, below)));
            if (Math.Abs(stage.DesiredSize.Width - width) < 0.5)
            {
                break;
            }

            width = stage.DesiredSize.Width;
        }

        columnWidth = width;
        return new Size(width, stage.DesiredSize.Height + below);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0d;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (i > 0 && !Takes(child))
            {
                continue;
            }

            child.Arrange(new Rect(0, y, columnWidth, child.DesiredSize.Height));
            y += child.DesiredSize.Height + Spacing;
        }

        return finalSize;
    }

    private double StageHeight(double available, double below) =>
        double.IsInfinity(available) ? double.PositiveInfinity : Math.Max(MinStageHeight, available - below);

    /// <summary>A altura dos filhos de baixo com o espaço antes de cada um; os que estão vazios ou escondidos não deixam vão.</summary>
    private double MeasureBelow(double width, double maxHeight)
    {
        var below = 0d;
        for (var i = 1; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(width, Math.Max(0, maxHeight - below - Spacing)));
            if (Takes(Children[i]))
            {
                below += Children[i].DesiredSize.Height + Spacing;
            }
        }

        return below;
    }

    private static bool Takes(Control child) => child.IsVisible && child.DesiredSize.Height > 0;
}
