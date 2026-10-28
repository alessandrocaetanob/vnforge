using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Views;

namespace VNForge.App.Tests.Rendering;

/// <summary>A regra das telas (#463): o marcador do jogo sai como selo, nunca como <c>\p</c> escrito no texto.</summary>
internal static class RawMarkers
{
    /// <summary>O cartão de "mesmo original" tem <paramref name="minChips"/> selos ou mais e nenhum texto com barra invertida.</summary>
    public static void AssertCardUsesChips(ConsistencyCardView card, int minChips)
    {
        var chips = card.GetVisualDescendants().OfType<MarkerChip>().Where(c => c.IsEffectivelyVisible).ToList();
        Assert.True(chips.Count >= minChips, $"o original e cada variante mostram o marcador como selo (achei {chips.Count}, esperava {minChips} ou mais)");

        var raw = card.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(TextOf).Where(t => t.Contains('\\', StringComparison.Ordinal)).ToList();
        Assert.True(raw.Count == 0, $"marcador cru no cartão: {string.Join(" | ", raw)}");
    }

    private static string TextOf(TextBlock block) =>
        block.Text ?? string.Concat((block.Inlines ?? []).OfType<Run>().Select(r => r.Text));
}
