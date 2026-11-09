using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Views;

/// <summary>O cartão da passada de editor (#532), no inspetor do Editor e no Modo revisão. O <see cref="IsReview"/> escolhe a variante do Modo revisão.</summary>
public sealed partial class EditorSuggestionCardView : UserControl
{
    public static readonly StyledProperty<bool> IsReviewProperty = AvaloniaProperty.Register<EditorSuggestionCardView, bool>(nameof(IsReview));

    static EditorSuggestionCardView() =>
        IsReviewProperty.Changed.AddClassHandler<EditorSuggestionCardView>((view, e) => view.PseudoClasses.Set(":review", e.NewValue is true));

    public EditorSuggestionCardView() => InitializeComponent();

    /// <summary>Verdadeiro no Modo revisão: título com o tipo, "n de N na cena" e só o texto proposto, com as palavras que mudaram marcadas.</summary>
    public bool IsReview
    {
        get => GetValue(IsReviewProperty);
        set => SetValue(IsReviewProperty, value);
    }
}
