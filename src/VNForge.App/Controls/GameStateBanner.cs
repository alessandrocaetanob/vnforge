using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>O que o jogo tem hoje na pasta dele.</summary>
public enum GameState
{
    /// <summary>O jogo está como veio: sem tradução.</summary>
    Original,

    /// <summary>O jogo está com a tradução do projeto.</summary>
    Translated,

    /// <summary>Algo impede o teste (a Conferência tem erros).</summary>
    Blocked,

    /// <summary>O jogo tem a tradução, mas o VNForge não consegue devolver o original (a cópia de segurança sumiu ou não lê).</summary>
    Warning,
}

/// <summary>
/// A faixa do estado do jogo em "Testar no jogo" (v3.css, <c>.gamestate</c>): um ponto de estado, o título ("O jogo está com a tradução de 22/10"), uma frase
/// e, em <c>Content</c>, o botão ("Voltar ao jogo original"). O estado vira <c>:original</c>, <c>:translated</c> ou <c>:blocked</c> e pinta o ponto.
/// </summary>
public sealed class GameStateBanner : ContentControl
{
    public static readonly StyledProperty<GameState> StateProperty = AvaloniaProperty.Register<GameStateBanner, GameState>(nameof(State));
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<GameStateBanner, string?>(nameof(Title));
    public static readonly StyledProperty<string?> MessageProperty = AvaloniaProperty.Register<GameStateBanner, string?>(nameof(Message));

    public GameStateBanner() => ApplyState();

    public GameState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty)
        {
            ApplyState();
        }
    }

    private void ApplyState()
    {
        PseudoClasses.Set(":original", State == GameState.Original);
        PseudoClasses.Set(":translated", State == GameState.Translated);
        PseudoClasses.Set(":blocked", State == GameState.Blocked);
        PseudoClasses.Set(":warning", State == GameState.Warning);
    }
}