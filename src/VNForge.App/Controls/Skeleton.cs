using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace VNForge.App.Controls;

/// <summary>
/// Esqueleto de carregamento: <see cref="Rows"/> faixas cinzas de <see cref="RowHeight"/> no lugar de uma lista ou tabela que ainda não chegou.
/// A pulsação vem do estilo e só roda com a classe <c>motion</c> da janela (ver <c>Motion.Follow</c>).
/// </summary>
public sealed class Skeleton : TemplatedControl
{
    public static readonly StyledProperty<int> RowsProperty = AvaloniaProperty.Register<Skeleton, int>(nameof(Rows), 6);
    public static readonly StyledProperty<double> RowHeightProperty = AvaloniaProperty.Register<Skeleton, double>(nameof(RowHeight), 12);
    public static readonly StyledProperty<double> RowSpacingProperty = AvaloniaProperty.Register<Skeleton, double>(nameof(RowSpacing), 14);

    private static readonly double[] Widths = [1.0, 0.82, 0.94, 0.7, 0.88, 0.76];

    private StackPanel? panel;

    static Skeleton()
    {
        RowsProperty.Changed.AddClassHandler<Skeleton>((s, _) => s.Rebuild());
        RowHeightProperty.Changed.AddClassHandler<Skeleton>((s, _) => s.Rebuild());
        RowSpacingProperty.Changed.AddClassHandler<Skeleton>((s, _) => s.Rebuild());
    }

    public int Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        panel = e.NameScope.Find<StackPanel>("PART_Rows");
        Rebuild();
    }

    private void Rebuild()
    {
        if (panel is null)
        {
            return;
        }

        panel.Spacing = RowSpacing;
        panel.Children.Clear();
        for (var i = 0; i < Math.Max(0, Rows); i++)
        {
            var share = Widths[i % Widths.Length];
            var row = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(share, GridUnitType.Star), new ColumnDefinition(1 - share, GridUnitType.Star)],
            };
            row.Children.Add(new Border { Classes = { "skeleton-bar" }, Height = RowHeight });
            panel.Children.Add(row);
        }
    }
}
