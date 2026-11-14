using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace VNForge.App.Tests.Screens;

/// <summary>
/// Pega texto cortado: um <see cref="TextBlock"/> sem quebra nem reticências que recebe menos largura do que o texto pede
/// aparece com letras a menos ("Apro", "Conferênci"). A largura natural vem de um bloco igual medido sem limite.
/// </summary>
internal static class ClippedTextFinder
{
    private const double Tolerance = 0.5;

    internal static List<string> FindClipped(Visual root)
    {
        var found = new List<string>();
        foreach (var block in root.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!block.IsEffectivelyVisible || string.IsNullOrEmpty(block.Text) || block.Bounds.Width <= 0
                || block.TextWrapping != Avalonia.Media.TextWrapping.NoWrap || block.TextTrimming != Avalonia.Media.TextTrimming.None)
            {
                continue;
            }

            var natural = new TextBlock
            {
                Text = block.Text,
                FontFamily = block.FontFamily,
                FontSize = block.FontSize,
                FontWeight = block.FontWeight,
                FontStyle = block.FontStyle,
            };
            natural.Measure(Size.Infinity);
            if (block.Bounds.Width + Tolerance < natural.DesiredSize.Width || ClippedByAncestor(block))
            {
                found.Add($"\"{block.Text}\" ({block.Bounds.Width:0.#} de {natural.DesiredSize.Width:0.#})");
            }
        }

        return found;
    }

    /// <summary>O texto cabe no próprio bloco, mas um pai com recorte (botão, item do trilho) é mais estreito do que ele.</summary>
    private static bool ClippedByAncestor(Control block)
    {
        var width = block.Bounds.Width;
        var clips = false;
        for (var parent = block.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
        {
            clips |= parent is Control { ClipToBounds: true };
            if (parent is Button or ListBoxItem && parent is Control c)
            {
                // Um item do trilho sem recorte deixa o rótulo passar do fundo (como o quadro faz com "Personagens"): a letra aparece inteira.
                if (c is ListBoxItem && !clips)
                {
                    return false;
                }

                var origin = block.TranslatePoint(default, c);
                return origin is { } p && (p.X < -Tolerance || p.X + width > c.Bounds.Width + Tolerance);
            }
        }

        return false;
    }
}
