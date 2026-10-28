using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace VNForge.App.Controls;

/// <summary>
/// O menu de contexto do nome de quem fala: "Copiar nome original" copia o nome exatamente como o texto extraído o guarda, mesmo quando a tela mostra a
/// tradução, e vale sem selecionar nada; "Copiar tradução" aparece só quando o personagem está cadastrado. Sem <see cref="OriginalProperty"/> (a narração)
/// o bloco não tem menu. "Revisar falas deste personagem" aparece quando <see cref="ReviewCommand"/> está ligado e pode rodar. Ligue com <c>c:SpeakerMenu.Original="{Binding ...}"</c> e <c>c:SpeakerMenu.Translation="{Binding ...}"</c>.
/// </summary>
public static class SpeakerMenu
{
    public const string CopyOriginalHeader = "Copiar nome original";
    public const string CopyTranslationHeader = "Copiar tradução";
    public const string CopySelectionHeader = "Copiar seleção";
    public const string ReviewHeader = "Revisar falas deste personagem";

    /// <summary>O nome como está em <c>TextUnit.Speaker</c>; vazio é narração.</summary>
    public static readonly AttachedProperty<string?> OriginalProperty =
        AvaloniaProperty.RegisterAttached<SelectableTextBlock, string?>("Original", typeof(SpeakerMenu));

    /// <summary>O nome em português, quando o personagem está cadastrado.</summary>
    public static readonly AttachedProperty<string?> TranslationProperty =
        AvaloniaProperty.RegisterAttached<SelectableTextBlock, string?>("Translation", typeof(SpeakerMenu));

    /// <summary>O comando que abre o modo revisão nas falas do personagem; sem ele (ou sem poder rodar) o item não aparece.</summary>
    public static readonly AttachedProperty<ICommand?> ReviewCommandProperty =
        AvaloniaProperty.RegisterAttached<SelectableTextBlock, ICommand?>("ReviewCommand", typeof(SpeakerMenu));

    static SpeakerMenu()
    {
        OriginalProperty.Changed.AddClassHandler<SelectableTextBlock>((block, _) => Refresh(block));
    }

    public static string? GetOriginal(SelectableTextBlock block) => block.GetValue(OriginalProperty);

    public static void SetOriginal(SelectableTextBlock block, string? value) => block.SetValue(OriginalProperty, value);

    public static string? GetTranslation(SelectableTextBlock block) => block.GetValue(TranslationProperty);

    public static void SetTranslation(SelectableTextBlock block, string? value) => block.SetValue(TranslationProperty, value);

    public static ICommand? GetReviewCommand(SelectableTextBlock block) => block.GetValue(ReviewCommandProperty);

    public static void SetReviewCommand(SelectableTextBlock block, ICommand? value) => block.SetValue(ReviewCommandProperty, value);

    private static void Refresh(SelectableTextBlock block)
    {
        if (string.IsNullOrEmpty(GetOriginal(block)))
        {
            if (block.ContextFlyout is SpeakerFlyout)
            {
                block.ContextFlyout = null;
            }

            return;
        }

        if (block.ContextFlyout is not SpeakerFlyout)
        {
            block.ContextFlyout = new SpeakerFlyout(block);
        }
    }

    private sealed class SpeakerFlyout : MenuFlyout
    {
        public SpeakerFlyout(SelectableTextBlock block)
        {
            var original = new MenuItem { Header = CopyOriginalHeader };
            original.Click += (_, _) => _ = CopyTextAsync(block, GetOriginal(block));
            var translation = new MenuItem { Header = CopyTranslationHeader };
            translation.Click += (_, _) => _ = CopyTextAsync(block, GetTranslation(block));
            var selection = new MenuItem { Header = CopySelectionHeader };
            selection.Click += (_, _) => block.Copy();
            var review = new MenuItem { Header = ReviewHeader };
            review.Click += (_, _) => GetReviewCommand(block)?.Execute(null);
            Items.Add(original);
            Items.Add(translation);
            Items.Add(selection);
            Items.Add(review);

            Opening += (_, _) =>
            {
                var target = GetTranslation(block);
                translation.IsVisible = !string.IsNullOrEmpty(target) && target != GetOriginal(block);
                selection.IsVisible = !string.IsNullOrEmpty(block.SelectedText);
                review.IsVisible = GetReviewCommand(block) is { } command && command.CanExecute(null);
            };
        }
    }

    private static async Task CopyTextAsync(Control owner, string? text)
    {
        if (string.IsNullOrEmpty(text) || TopLevel.GetTopLevel(owner)?.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A área de transferência pode estar travada por outro programa; copiar de novo costuma funcionar.
        }
    }
}
