using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>
/// Cartão de número (KPI) do Painel: título em overline, o número grande em 28/32 na fonte de exibição, uma linha de apoio, a visualização (<c>Content</c>,
/// por exemplo uma barra e a legenda) e um rodapé com o link de ação, separado por <c>stroke-subtle</c> e sem margem lateral.
/// </summary>
public sealed class StatCard : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<StatCard, string?>(nameof(Title));
    public static readonly StyledProperty<string?> ValueProperty = AvaloniaProperty.Register<StatCard, string?>(nameof(Value));
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<StatCard, string?>(nameof(Description));
    public static readonly StyledProperty<object?> FooterProperty = AvaloniaProperty.Register<StatCard, object?>(nameof(Footer));

    /// <summary>O rótulo do cartão ("Linhas", "Uso da IA", "Conferência").</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>O número grande ("48.211", "US$ 26,40", "39 problemas").</summary>
    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>O conteúdo do rodapé, em geral um link; sem ele o rodapé some.</summary>
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }
}
