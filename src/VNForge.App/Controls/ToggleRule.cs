using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace VNForge.App.Controls;

/// <summary>
/// Linha de configuração liga/desliga (docs/design/components/ToggleRule): nome da regra, exemplo do efeito e o interruptor à direita.
/// O texto auxiliar mostra um antes/depois real. Dentro de um <c>Border.panel</c> as linhas se separam por <c>stroke-subtle</c>; a última não tem divisória.
/// </summary>
public sealed class ToggleRule : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<ToggleRule, string?>(nameof(Title));
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<ToggleRule, string?>(nameof(Description));

    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<ToggleRule, bool>(nameof(IsChecked), defaultBindingMode: BindingMode.TwoWay);

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Um antes/depois real ("Remove espaço antes de ! e ?"), não uma explicação abstrata.</summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }
}
