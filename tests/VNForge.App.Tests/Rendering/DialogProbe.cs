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

        // O quadro: borda de 1 px mais padding de 24 em todos os lados, título no canto de cima e à esquerda, X de 28 px no canto de cima e à direita.
        const double inset = 25;
        var title = card.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("title-md"));
        var titleOrigin = title.TranslatePoint(default, card)!.Value;
        Assert.True(Math.Abs(titleOrigin.X - inset) <= Tolerance, $"Título a {titleOrigin.X:0.#} px da esquerda; o quadro pede {inset}.");
        Assert.True(titleOrigin.Y >= inset - Tolerance && titleOrigin.Y <= inset + 4, $"Título a {titleOrigin.Y:0.#} px do alto; o quadro pede {inset}.");
        if (showsClose)
        {
            var closeOrigin = close.TranslatePoint(default, card)!.Value;
            Assert.True(Math.Abs(close.Bounds.Width - 28) <= Tolerance, $"X com {close.Bounds.Width:0.#} px; o quadro pede 28.");
            Assert.True(Math.Abs(closeOrigin.Y - inset) <= Tolerance, $"X a {closeOrigin.Y:0.#} px do alto; o quadro pede {inset}.");
            Assert.True(Math.Abs(card.Bounds.Width - closeOrigin.X - close.Bounds.Width - inset) <= Tolerance, "X fora dos 25 px da direita.");
        }

        var footer = card.GetVisualDescendants().OfType<Border>().SingleOrDefault(b => b.Name == "PART_Footer");
        if (footer is { IsEffectivelyVisible: true })
        {
            var footerOrigin = footer.TranslatePoint(default, card)!.Value;
            Assert.True(Math.Abs(card.Bounds.Width - footerOrigin.X - footer.Bounds.Width - inset) <= Tolerance, "Rodapé fora dos 25 px da direita.");
            Assert.True(Math.Abs(card.Bounds.Height - footerOrigin.Y - footer.Bounds.Height - inset) <= Tolerance, "Rodapé fora dos 25 px de baixo.");
        }
        Assert.DoesNotContain(card.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("divider-top"));
    }
}
