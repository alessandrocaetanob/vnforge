using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>
/// A grade de capas do quadro (<c>repeat(auto-fill, minmax(200px, 1fr))</c>, 24 px entre linhas e 20 entre colunas): quantas colunas cabem com a largura mínima, cada cartão com a largura que sobra
/// dividida por igual. O cartão é a capa 3:4 mais a faixa de texto, então a altura sai da largura. A memória das capas é das próprias capas, que só carregam perto da área visível
/// (<see cref="VndbImageControl.LoadOnlyInViewport"/>): a Biblioteca com centenas de jogos não segura centenas de bitmaps.
/// </summary>
public sealed class CoverGridPanel : Panel
{
    public const double MinItemWidth = 200;
    public const double ColumnSpacing = 20;
    public const double RowSpacing = 24;

    /// <summary>O texto sob a capa: 8 de espaço, o título de 20 e a linha de 16.</summary>
    public const double TextHeight = 8 + 20 + 16;

    private int columns = 1;
    private double itemWidth = MinItemWidth;
    private double itemHeight = (MinItemWidth * 4 / 3) + TextHeight;

    /// <summary>As colunas e a largura de cada uma para a largura disponível.</summary>
    public static (int Columns, double ItemWidth) Measure(double available)
    {
        var count = Math.Max(1, (int)Math.Floor((available + ColumnSpacing) / (MinItemWidth + ColumnSpacing)));
        return (count, (available - (ColumnSpacing * (count - 1))) / count);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width;
        (columns, itemWidth) = Measure(width);
        itemHeight = (itemWidth * 4 / 3) + TextHeight;
        foreach (var child in Children)
        {
            child.Measure(new Size(itemWidth, itemHeight));
        }

        var rows = (Children.Count + columns - 1) / columns;
        return new Size(width, rows == 0 ? 0 : (rows * itemHeight) + ((rows - 1) * RowSpacing));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var index = 0; index < Children.Count; index++)
        {
            Children[index].Arrange(new Rect((index % columns) * (itemWidth + ColumnSpacing), (index / columns) * (itemHeight + RowSpacing), itemWidth, itemHeight));
        }

        return finalSize;
    }
}
