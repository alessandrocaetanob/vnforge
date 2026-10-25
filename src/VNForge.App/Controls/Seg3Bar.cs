using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VNForge.App.Controls;

/// <summary>
/// Barra de três estados (v3.css, <c>.seg3</c>): pronto em <c>seg-approved</c>, traduzido pela IA esperando revisão em <c>seg-ai</c> e o que ainda
/// está em japonês no fundo, em <c>seg-pending</c>. Tem 8 px de altura e cantos redondos; a largura é a que a linha dá.
/// </summary>
public sealed class Seg3Bar : ThreeStateProgress
{
    public const double BarHeight = 8;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, BarHeight);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var track = new RoundedRect(new Rect(0, 0, width, BarHeight), BarHeight / 2);
        context.DrawRectangle(Brush("Vnf.SegPending"), null, track);

        using var _ = context.PushClip(track);
        var done = width * DoneFraction;
        var review = width * ReviewFraction;
        context.DrawRectangle(Brush("Vnf.SegApproved"), null, new Rect(0, 0, done, BarHeight));
        context.DrawRectangle(Brush("Vnf.SegAi"), null, new Rect(done, 0, review, BarHeight));
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.Gray;
}