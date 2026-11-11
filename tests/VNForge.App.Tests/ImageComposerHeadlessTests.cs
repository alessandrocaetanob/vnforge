using VNForge.App.Services;
using VNForge.Core.Images;

namespace VNForge.App.Tests;

/// <summary>O compositor de verdade (Skia headless): apaga a área, escreve o texto e devolve um PNG que o app lê de volta.</summary>
public class ImageComposerHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const int Width = 200;
    private const int Height = 100;

    private static RasterPicture Background()
    {
        var bgra = new byte[Width * Height * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (180, 120, 60, 255);
        }

        // O "texto original": pixels escuros na área que vai ser apagada.
        for (var y = 40; y < 60; y++)
        {
            for (var x = 60; x < 140; x++)
            {
                var at = ((y * Width) + x) * 4;
                (bgra[at], bgra[at + 1], bgra[at + 2]) = (0, 0, 0);
            }
        }

        return new RasterPicture(Width, Height, bgra);
    }

    private static ImageEdit Edit(string text) =>
        new("img.ypf", "bg/portao.png", "ab", Width, Height, new ImageRegion(50, 30, 100, 40), ImageEraseModes.Surrounding, null, text, "Noto Sans", 20, true, "#ffffff", null, true);

    private static bool IsBackground(RasterPicture picture, int x, int y)
    {
        var at = ((y * picture.Width) + x) * 4;
        return Math.Abs(picture.Bgra[at] - 180) <= 2 && Math.Abs(picture.Bgra[at + 1] - 120) <= 2 && Math.Abs(picture.Bgra[at + 2] - 60) <= 2;
    }

    private static byte[] HeaderOnly(int width, int height)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 };
        bytes.AddRange("IHDR"u8.ToArray());
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        bytes.AddRange(header);
        bytes.AddRange(new byte[4]);
        return [.. bytes];
    }

    [Fact]
    public async Task SemiTransparentPixelsOutsideTheAreaComeBackWithTheSameAlphaAndColour()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var composer = new AvaloniaImageComposer();
                var bgra = new byte[Width * Height * 4];
                for (var i = 0; i < bgra.Length; i += 4)
                {
                    // Pré-multiplicado, como o Decode devolve: cor 200 com alfa 128 vira 100.
                    (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (100, 60, 30, 128);
                }

                var rendered = composer.Decode(composer.Render(new RasterPicture(Width, Height, bgra), Edit(string.Empty)));

                foreach (var (x, y) in new[] { (5, 5), (190, 90), (10, 95) })
                {
                    var at = ((y * Width) + x) * 4;
                    Assert.InRange(rendered.Bgra[at + 3], 127, 129);
                    Assert.InRange(rendered.Bgra[at], 99, 101);
                    Assert.InRange(rendered.Bgra[at + 1], 59, 61);
                    Assert.InRange(rendered.Bgra[at + 2], 29, 31);
                }
            },
            TestContext.Current.CancellationToken);
    }
}
