using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O quadro de uma janela com o popup de um <see cref="Flyout"/> aberto, na posição em que o Avalonia o pôs. O popup não é filho da janela: com a plataforma do
/// teste ele vira uma <see cref="PopupRoot"/> à parte, e <c>CaptureRenderedFrame</c> da janela não o inclui. Aqui o quadro da janela e o do popup são
/// compostos no ponto em que o popup abriu, então posição, largura e sombra saem como o usuário as vê.
/// </summary>
internal static class PopupCapture
{
    /// <summary>A raiz do popup que mostra <paramref name="inside"/>, ou <see langword="null"/> quando o conteúdo vive na própria janela (camada de sobreposição).</summary>
    public static PopupRoot? RootOf(Visual inside) => TopLevel.GetTopLevel(inside) as PopupRoot;

    /// <summary>Onde o popup fica na janela, em pixels da janela; sem raiz de popup, o retângulo do próprio conteúdo.</summary>
    public static Rect BoundsIn(Window window, Control inside)
    {
        if (RootOf(inside) is { } root)
        {
            var origin = PositionOf(root) - window.Position;
            return new Rect(origin.X, origin.Y, root.Bounds.Width, root.Bounds.Height);
        }

        var point = inside.TranslatePoint(default, window) ?? default;
        return new Rect(point, inside.Bounds.Size);
    }

    /// <summary>
    /// A posição que o posicionador do Avalonia deu ao popup, em pixels da tela. A implementação do popup na plataforma do teste não é pública, mas expõe <c>Position</c>;
    /// é a mesma que o Win32 recebe para posicionar a janela do popup.
    /// </summary>
    private static PixelPoint PositionOf(PopupRoot root) =>
        root.PlatformImpl?.GetType().GetProperty("Position")?.GetValue(root.PlatformImpl) is PixelPoint position
            ? position
            : throw new InvalidOperationException("A implementação do popup não informa a posição.");

    /// <summary>Salva a janela com o popup por cima, do jeito que o usuário o vê.</summary>
    public static void Save(Window window, Control inside, string path)
    {
        using var windowFrame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("O Avalonia.Headless não devolveu o quadro da janela.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (RootOf(inside) is not { } root)
        {
            windowFrame.Save(path, PngBitmapEncoderOptions.Default);
            return;
        }

        using var popupFrame = root.CaptureRenderedFrame() ?? throw new InvalidOperationException("O Avalonia.Headless não devolveu o quadro do popup.");
        var at = BoundsIn(window, inside);
        using var target = new RenderTargetBitmap(windowFrame.PixelSize, windowFrame.Dpi);
        using (var context = target.CreateDrawingContext())
        {
            context.DrawImage(windowFrame, new Rect(windowFrame.Size));
            context.DrawImage(popupFrame, new Rect(popupFrame.Size), new Rect(at.X, at.Y, popupFrame.Size.Width, popupFrame.Size.Height));
        }

        target.Save(path, PngBitmapEncoderOptions.Default);
    }
}
