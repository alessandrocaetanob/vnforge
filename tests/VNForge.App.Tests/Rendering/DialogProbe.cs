using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace VNForge.App.Tests.Rendering;

/// <summary>Mede o cartão de um <c>ScreenDialog</c> aberto na camada de sobreposição da janela e confere o que o quadro v6 pede de todo diálogo (item G2 da #580).</summary>
internal static class DialogProbe
{
    /// <summary>Folga aceita entre as margens dos dois lados, em pixels.</summary>
    public const double Tolerance = 1;

    public static Border Card(Window window) =>
        Assert.Single(OverlayLayer.GetOverlayLayer(window)!.GetVisualDescendants().OfType<Border>(), b => b.Name == "PART_Dialog");

    /// <summary>O cartão está centrado na janela (margens esquerda e direita, em cima e embaixo, iguais), tem a largura do quadro, não tem divisor e só mostra o X se for o caso.</summary>
    public static void AssertCentered(Window window, double expectedWidth, bool showsClose = false)
    {
        var card = Card(window);
        var origin = card.TranslatePoint(default, window) ?? throw new InvalidOperationException("O cartão do diálogo não está na árvore da janela.");
        var size = window.ClientSize;
        var left = origin.X;
        var right = size.Width - origin.X - card.Bounds.Width;
        var top = origin.Y;
        var bottom = size.Height - origin.Y - card.Bounds.Height;

        Assert.True(Math.Abs(left - right) <= Tolerance, $"Margens horizontais diferentes: esquerda {left:0.#}, direita {right:0.#}.");
        Assert.True(Math.Abs(top - bottom) <= Tolerance, $"Margens verticais diferentes: em cima {top:0.#}, embaixo {bottom:0.#}.");
        Assert.True(Math.Abs(card.Bounds.Width - expectedWidth) <= Tolerance, $"Largura {card.Bounds.Width:0.#}, o quadro pede {expectedWidth:0.#}.");

        var close = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Close");
        Assert.Equal(showsClose, close.IsEffectivelyVisible);
        Assert.DoesNotContain(card.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("divider-top"));
    }
}
