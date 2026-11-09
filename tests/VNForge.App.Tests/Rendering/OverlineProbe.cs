using Avalonia.Controls;
using Avalonia.VisualTree;
using VNForge.App.Controls;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O overline do quadro é sempre caixa alta (<c>.v3-lbl</c>, <c>text-transform: uppercase</c>). Como o <see cref="TextBlock"/> não transforma o texto,
/// um <c>TextBlock</c> com a classe <c>overline</c> e letra minúscula sairia em caixa baixa; só o <see cref="OverlineText"/> desenha em caixa alta.
/// </summary>
internal static class OverlineProbe
{
    /// <summary>Os rótulos overline visíveis de <paramref name="root"/> que apareceriam com letra minúscula.</summary>
    public static List<string> LowercaseLabels(Control root) =>
        root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.Classes.Contains("overline") && t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text))
            .Where(t => t is not OverlineText && t.Text != t.Text!.ToUpperInvariant())
            .Select(t => t.Text!)
            .Distinct()
            .ToList();

    public static void AssertUpperCase(Control root)
    {
        var lowercase = LowercaseLabels(root);
        Assert.True(lowercase.Count == 0, "Rótulo overline em caixa baixa (use c:OverlineText): " + string.Join(" | ", lowercase));
    }
}
