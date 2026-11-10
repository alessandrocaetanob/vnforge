using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Mede se um controle aparece inteiro na janela como ela abre, sem rolar: o retângulo dele contra a área que sobra depois de recortar pela janela e pelo
/// viewport de cada ScrollViewer acima dele. Não chama BringIntoView, que só provaria que dá para rolar até o controle.
/// </summary>
internal static class VisibleArea
{
    private const double Tolerance = 0.5;

    public static Measure Of(Control control, Window window)
    {
        var box = new Rect(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
        var visible = new Rect(window.ClientSize);
        foreach (var scroll in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            visible = visible.Intersect(new Rect(scroll.TranslatePoint(default, window)!.Value, scroll.Viewport));
        }

        return new Measure(box, visible);
    }

    /// <param name="Box">Onde o controle está na janela.</param>
    /// <param name="Visible">A área em que o que for desenhado aparece.</param>
    public readonly record struct Measure(Rect Box, Rect Visible)
    {
        public bool IsFullyVisible => Visible.Inflate(Tolerance).Contains(Box);

        /// <summary>Quantos pixels da base do controle ficam abaixo da área visível (zero se não passa).</summary>
        public double CutBelow => Math.Max(0, Box.Bottom - Visible.Bottom);

        public override string ToString() => $"controle em {Box.X:0}, {Box.Y:0} ({Box.Width:0}x{Box.Height:0}), área visível até y={Visible.Bottom:0} (falta {CutBelow:0} px)";
    }
}
