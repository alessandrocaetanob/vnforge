using Avalonia;
using Avalonia.Controls.Primitives;
using VNForge.App.Services;

namespace VNForge.App.Controls;

/// <summary>
/// Chip de filtro com a contagem de linhas que passam (artboards do Editor e da Conferência): o rótulo e o número em mono.
/// Marcado (<c>IsChecked</c>) ganha <c>accent-soft</c>, borda <c>accent</c> e peso 600.
/// </summary>
public sealed class FilterChip : ToggleButton
{
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<FilterChip, string?>(nameof(Label));
    public static readonly StyledProperty<int?> CountProperty = AvaloniaProperty.Register<FilterChip, int?>(nameof(Count));

    public static readonly DirectProperty<FilterChip, string?> CountTextProperty =
        AvaloniaProperty.RegisterDirect<FilterChip, string?>(nameof(CountText), o => o.CountText);

    private string? countText;

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Quantas linhas passam pelo filtro; sem valor, o chip não mostra número.</summary>
    public int? Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    /// <summary>A contagem no formato pt-BR (<c>18.320</c>); vazia quando não há contagem.</summary>
    public string? CountText
    {
        get => countText;
        private set => SetAndRaise(CountTextProperty, ref countText, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CountProperty)
        {
            CountText = Count is { } n ? ProjectLabels.Number(n) : null;
        }
    }
}
