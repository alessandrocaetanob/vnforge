using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using VNForge.Core.Images;

namespace VNForge.App.Services;

/// <summary>Os pixels de uma imagem decodificada, em BGRA (4 bytes por pixel, linhas seguidas).</summary>
public sealed record RasterPicture(int Width, int Height, byte[] Bgra);

/// <summary>
/// O que o editor de imagens (#535) pede à plataforma gráfica: decodificar a imagem do jogo, escrever o texto sobre a área apagada e medir
/// o texto. Atrás de uma interface para os testes do editor não dependerem do Avalonia.
/// </summary>
public interface IImageComposer
{
    /// <summary>Famílias de fonte que o editor oferece: as embutidas do app primeiro e depois as do sistema.</summary>
    IReadOnlyList<string> FontFamilies { get; }

    /// <exception cref="InvalidDataException">Os bytes não são uma imagem que o app sabe decodificar.</exception>
    RasterPicture Decode(byte[] encoded);

    /// <summary>A imagem pronta, em PNG: a área da edição apagada e o texto escrito, centralizado nela.</summary>
    byte[] Render(RasterPicture source, ImageEdit edit);

    /// <summary>Largura e altura que o texto da edição ocupa, em pixels da imagem.</summary>
    (double Width, double Height) Measure(ImageEdit edit);
}

/// <summary>O <see cref="IImageComposer"/> do app, sobre o Avalonia (Skia).</summary>
public sealed class AvaloniaImageComposer : IImageComposer
{
    private const string EmbeddedFonts = "avares://VNForge.App/Assets/Fonts#";

    private static readonly string[] Embedded = ["Zen Kaku Gothic New", "Noto Sans", "Noto Sans JP", "JetBrains Mono"];

    private static readonly Vector Dpi = new(96, 96);

    // Preguiçoso: o FontManager só existe com o Avalonia de pé, e a janela principal cria o editor antes disso nos testes.
    private readonly Lazy<IReadOnlyList<string>> families = new(() =>
    [
        .. Embedded,
        .. FontManager.Current.SystemFonts.Select(f => f.Name).Where(n => !Embedded.Contains(n, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase),
    ]);

    public IReadOnlyList<string> FontFamilies => families.Value;

    public RasterPicture Decode(byte[] encoded)
    {
        try
        {
            using var stream = new MemoryStream(encoded);
            using var bitmap = WriteableBitmap.Decode(stream);
            using var frame = bitmap.Lock();
            var width = frame.Size.Width;
            var height = frame.Size.Height;
            var bgra = new byte[width * height * 4];
            var swap = frame.Format == PixelFormat.Rgba8888;
            var row = new byte[width * 4];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(frame.Address + (y * frame.RowBytes), row, 0, row.Length);
                if (swap)
                {
                    for (var x = 0; x < row.Length; x += 4)
                    {
                        (row[x], row[x + 2]) = (row[x + 2], row[x]);
                    }
                }

                row.CopyTo(bgra, y * row.Length);
            }

            return new RasterPicture(width, height, bgra);
        }
        catch (Exception ex) when (ex is not InvalidDataException and not OutOfMemoryException)
        {
            throw new InvalidDataException("O app não consegue abrir esta imagem.", ex);
        }
    }

    public byte[] Render(RasterPicture source, ImageEdit edit)
    {
        var pixels = (byte[])source.Bgra.Clone();
        var solid = edit.EraseColor is { } hex ? ToRgb(hex) : default;
        ImageEraser.Erase(pixels, source.Width, source.Height, edit.Region, edit.Erase, solid);

        var size = new PixelSize(source.Width, source.Height);
        using var erased = new WriteableBitmap(size, Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var frame = erased.Lock())
        {
            for (var y = 0; y < source.Height; y++)
            {
                Marshal.Copy(pixels, y * source.Width * 4, frame.Address + (y * frame.RowBytes), source.Width * 4);
            }
        }

        using var target = new RenderTargetBitmap(size, Dpi);
        using (var context = target.CreateDrawingContext())
        {
            context.DrawImage(erased, new Rect(0, 0, source.Width, source.Height));
            DrawText(context, edit);
        }

        using var output = new MemoryStream();
        target.Save(output, PngBitmapEncoderOptions.Default);
        return output.ToArray();
    }

    public (double Width, double Height) Measure(ImageEdit edit)
    {
        var text = Format(edit, Brushes.Black);
        return (text.Width, text.Height);
    }

    private static void DrawText(DrawingContext context, ImageEdit edit)
    {
        if (edit.Text.Length == 0)
        {
            return;
        }

        var text = Format(edit, new SolidColorBrush(Color.Parse(edit.Color)));
        var origin = new Point(
            edit.Region.X + ((edit.Region.Width - text.Width) / 2),
            edit.Region.Y + ((edit.Region.Height - text.Height) / 2));
        if (edit.Outline is { } outline)
        {
            var pen = new Pen(new SolidColorBrush(Color.Parse(outline)), Math.Max(1, edit.FontSize / 6), lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, pen, text.BuildGeometry(origin)!);
        }

        context.DrawText(text, origin);
    }

    private static FormattedText Format(ImageEdit edit, IBrush brush) =>
        new(
            edit.Text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(Family(edit.Font), FontStyle.Normal, edit.Bold ? FontWeight.Bold : FontWeight.Normal),
            edit.FontSize,
            brush)
        { TextAlignment = TextAlignment.Center };

    private static FontFamily Family(string name) =>
        Embedded.Contains(name, StringComparer.OrdinalIgnoreCase) ? new FontFamily(EmbeddedFonts + name) : new FontFamily(name);

    private static (byte R, byte G, byte B) ToRgb(string hex)
    {
        var color = Color.Parse(hex);
        return (color.R, color.G, color.B);
    }
}
