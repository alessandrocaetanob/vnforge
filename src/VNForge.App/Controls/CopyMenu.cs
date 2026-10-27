using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>
/// Troca o menu de contexto padrão do <see cref="SelectableTextBlock"/> (em inglês) por um "Copiar" em português; Ctrl+C já vem do controle.
/// Ligue com <c>c:CopyMenu.Enabled="True"</c> ou, num controle montado em código (uma parte do template), com <see cref="Attach"/>.
/// </summary>
public static class CopyMenu
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<SelectableTextBlock, bool>("Enabled", typeof(CopyMenu));

    static CopyMenu()
    {
        EnabledProperty.Changed.AddClassHandler<SelectableTextBlock>((block, e) =>
        {
            if (e.NewValue is true)
            {
                Attach(block);
            }
        });
    }

    public static bool GetEnabled(SelectableTextBlock block) => block.GetValue(EnabledProperty);

    public static void SetEnabled(SelectableTextBlock block, bool value) => block.SetValue(EnabledProperty, value);

    /// <summary>Dá ao bloco o menu "Copiar". "Copiar" só vale com texto selecionado.</summary>
    public static void Attach(SelectableTextBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var copy = new MenuItem { Header = "Copiar" };
        copy.Click += (_, _) => block.Copy();
        var flyout = new MenuFlyout { Items = { copy } };
        flyout.Opening += (_, _) => copy.IsEnabled = !string.IsNullOrEmpty(block.SelectedText);
        block.ContextFlyout = flyout;
    }
}
