using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using VNForge.Core.Projects;

namespace VNForge.App.Controls;

/// <summary>A cor do quadradinho da legenda: os segmentos da distribuição de status, os sinais do QA ou o destaque.</summary>
public enum LegendTone
{
    Approved,
    Reviewed,
    Translated,
    Machine,
    Pending,
    Error,
    Warning,
    Accent,
}

public sealed record LegendEntry(string Text, LegendTone Tone)
{
    /// <summary>
    /// As cinco entradas da distribuição de status, na ordem fixa do design: Aprovadas, Revisadas, Traduzidas, IA, Pendentes.
    /// Os percentuais já vêm arredondados e somando 100 do <see cref="StatusDistribution"/>.
    /// </summary>
    public static IReadOnlyList<LegendEntry> ForDistribution(StatusDistribution distribution)
    {
        ArgumentNullException.ThrowIfNull(distribution);
        return
        [
            new($"Aprovadas {distribution.Approved}%", LegendTone.Approved),
            new($"Revisadas {distribution.Reviewed}%", LegendTone.Reviewed),
            new($"Traduzidas {distribution.Translated}%", LegendTone.Translated),
            new($"IA {distribution.Machine}%", LegendTone.Machine),
            new($"Pendentes {distribution.Pending}%", LegendTone.Pending),
        ];
    }
}

/// <summary>Um item da <see cref="Legend"/>: quadrado de 8px com a cor do tom e o texto em 12/16.</summary>
public sealed class LegendItem : ContentControl
{
    public static readonly StyledProperty<LegendTone> ToneProperty = AvaloniaProperty.Register<LegendItem, LegendTone>(nameof(Tone));

    public LegendTone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }
}

/// <summary>
/// Legenda de uma barra empilhada (docs/design/components/StatusDistribution): itens lado a lado que quebram de linha.
/// Pode receber <see cref="LegendEntry"/> em <c>ItemsSource</c> ou, para a distribuição de status, um <see cref="StatusDistribution"/> em <see cref="Value"/>.
/// </summary>
public sealed class Legend : ItemsControl
{
    public static readonly StyledProperty<StatusDistribution?> ValueProperty = AvaloniaProperty.Register<Legend, StatusDistribution?>(nameof(Value));

    public StatusDistribution? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            ItemsSource = Value is { } v ? LegendEntry.ForDistribution(v) : null;
        }
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = null;
        return item is not LegendItem;
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new LegendItem();

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is LegendItem legendItem && item is LegendEntry entry)
        {
            legendItem.Content = entry.Text;
            legendItem.Tone = entry.Tone;
        }
    }
}
