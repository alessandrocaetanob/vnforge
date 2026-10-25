using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace VNForge.App.Controls;

/// <summary>
/// Um item de "Precisa de você" (v3.css, <c>.ready</c>): ícone de gravidade, título, uma frase de explicação e o botão que resolve.
/// A gravidade vira <c>:error</c>, <c>:warning</c> ou <c>:ok</c> e escolhe o ícone e a cor; sem <see cref="ActionText"/> o botão some.
/// </summary>
public sealed class TodoItem : TemplatedControl
{
    public static readonly StyledProperty<SeverityLevel> SeverityProperty = AvaloniaProperty.Register<TodoItem, SeverityLevel>(nameof(Severity), SeverityLevel.Warning);
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<TodoItem, string?>(nameof(Title));
    public static readonly StyledProperty<string?> MessageProperty = AvaloniaProperty.Register<TodoItem, string?>(nameof(Message));
    public static readonly StyledProperty<string?> ActionTextProperty = AvaloniaProperty.Register<TodoItem, string?>(nameof(ActionText));
    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<TodoItem, ICommand?>(nameof(Command));

    public TodoItem() => ApplySeverity();

    public SeverityLevel Severity
    {
        get => GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>O rótulo do botão ("Corrigir", "Definir agora"); nulo esconde o botão.</summary>
    public string? ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SeverityProperty)
        {
            ApplySeverity();
        }
    }

    private void ApplySeverity()
    {
        PseudoClasses.Set(":ok", Severity == SeverityLevel.Ok);
        PseudoClasses.Set(":warning", Severity == SeverityLevel.Warning);
        PseudoClasses.Set(":error", Severity == SeverityLevel.Error);
    }
}