using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>Um pedaço do original com o que ele significa: <c>今日</c> · hoje.</summary>
public sealed record ExplainPart(string Source, string Meaning);

/// <summary>
/// "Entender o japonês" (v3.css, <c>.explain</c>): o cartão tracejado com a explicação da IA. O cabeçalho diz de onde ela vem (<see cref="Model"/>, em cor de IA),
/// os itens são os pedaços do original com o significado de cada um, e <see cref="Footnote"/> avisa que é só para ler, não entra na tradução.
/// </summary>
public sealed class ExplainCard : ItemsControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<ExplainCard, string?>(nameof(Title), "Explicação da IA");
    public static readonly StyledProperty<string?> ModelProperty = AvaloniaProperty.Register<ExplainCard, string?>(nameof(Model));
    public static readonly StyledProperty<string?> FootnoteProperty = AvaloniaProperty.Register<ExplainCard, string?>(nameof(Footnote));

    protected override Type StyleKeyOverride => typeof(ExplainCard);

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public string? Footnote
    {
        get => GetValue(FootnoteProperty);
        set => SetValue(FootnoteProperty, value);
    }
}