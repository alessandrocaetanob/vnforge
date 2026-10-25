using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace VNForge.App.Controls;

/// <summary>
/// O aviso "Não cabe na caixa do jogo" (v3.css, <c>.fitwarn</c>) com as duas saídas em botões: "Dividir em 2 páginas" e "Encurtar com a IA". É o mesmo
/// controle no inspetor do Editor, no modo revisão e na Conferência; quem o usa liga os textos e os comandos ao <c>FitWarningViewModel</c> e decide
/// quando mostrá-lo. Com <see cref="ShowShorten"/> falso o botão da IA some, e <see cref="ErrorText"/> aparece sob os botões quando o pedido falha.
/// </summary>
public sealed class FitWarning : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<FitWarning, string?>(nameof(Title), "Não cabe na caixa do jogo");
    public static readonly StyledProperty<string?> MessageProperty = AvaloniaProperty.Register<FitWarning, string?>(nameof(Message));
    public static readonly StyledProperty<string?> SplitTextProperty = AvaloniaProperty.Register<FitWarning, string?>(nameof(SplitText), "Dividir em 2 páginas");
    public static readonly StyledProperty<string?> ShortenTextProperty = AvaloniaProperty.Register<FitWarning, string?>(nameof(ShortenText), "Encurtar com a IA");
    public static readonly StyledProperty<ICommand?> SplitCommandProperty = AvaloniaProperty.Register<FitWarning, ICommand?>(nameof(SplitCommand));
    public static readonly StyledProperty<ICommand?> ShortenCommandProperty = AvaloniaProperty.Register<FitWarning, ICommand?>(nameof(ShortenCommand));
    public static readonly StyledProperty<string?> ErrorTextProperty = AvaloniaProperty.Register<FitWarning, string?>(nameof(ErrorText));
    public static readonly StyledProperty<bool> ShowShortenProperty = AvaloniaProperty.Register<FitWarning, bool>(nameof(ShowShorten), true);

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Quanto passa: "Passa 1 linha: a caixa do jogo tem 3 linhas."</summary>
    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string? SplitText
    {
        get => GetValue(SplitTextProperty);
        set => SetValue(SplitTextProperty, value);
    }

    public string? ShortenText
    {
        get => GetValue(ShortenTextProperty);
        set => SetValue(ShortenTextProperty, value);
    }

    public ICommand? SplitCommand
    {
        get => GetValue(SplitCommandProperty);
        set => SetValue(SplitCommandProperty, value);
    }

    public ICommand? ShortenCommand
    {
        get => GetValue(ShortenCommandProperty);
        set => SetValue(ShortenCommandProperty, value);
    }

    /// <summary>Falso esconde o botão "Encurtar com a IA" (o app não tem como pedir à IA).</summary>
    public bool ShowShorten
    {
        get => GetValue(ShowShortenProperty);
        set => SetValue(ShowShortenProperty, value);
    }

    public string? ErrorText
    {
        get => GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }
}
