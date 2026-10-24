using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>
/// Estado vazio de uma página: o selo 訳 em marca d'água, uma frase e uma ação (opcional). A marca é um <c>Path</c> (<c>Brand.Seal</c>), então não
/// depende de fonte japonesa instalada.
/// </summary>
public sealed class EmptyState : ContentControl
{
    public static readonly StyledProperty<string?> MessageProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(Message));
    public static readonly StyledProperty<string?> ActionTextProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(ActionText));
    public static readonly StyledProperty<ICommand?> ActionCommandProperty = AvaloniaProperty.Register<EmptyState, ICommand?>(nameof(ActionCommand));

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>O texto do botão; sem ele (ou sem <see cref="ActionCommand"/>) o estado vazio só informa.</summary>
    public string? ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }
}
