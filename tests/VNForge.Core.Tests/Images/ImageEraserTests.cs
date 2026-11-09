using VNForge.Core.Images;

namespace VNForge.Core.Tests.Images;

public sealed class ImageEraserTests
{
    private static byte[] Gradient(int width, int height, Func<int, int, (byte B, byte G, byte R)> color)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (b, g, r) = color(x, y);
                var at = ((y * width) + x) * 4;
                bytes[at] = b;
                bytes[at + 1] = g;
                bytes[at + 2] = r;
                bytes[at + 3] = 255;
            }
        }

        return bytes;
    }

    private static (byte B, byte G, byte R, byte A) Pixel(byte[] bytes, int width, int x, int y)
    {
        var at = ((y * width) + x) * 4;
        return (bytes[at], bytes[at + 1], bytes[at + 2], bytes[at + 3]);
    }

    [Fact]
    public void SolidFillsOnlyTheRegion()
    {
        var image = Gradient(20, 10, (_, _) => (10, 20, 30));
        var before = (byte[])image.Clone();

        ImageEraser.Erase(image, 20, 10, new ImageRegion(5, 2, 4, 3), ImageEraseModes.Solid, (200, 100, 50));

        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                var inside = x is >= 5 and < 9 && y is >= 2 and < 5;
                Assert.Equal(inside ? ((byte)50, (byte)100, (byte)200, (byte)255) : Pixel(before, 20, x, y), Pixel(image, 20, x, y));
            }
        }
    }

    [Fact]
    public void SurroundingFollowsAHorizontalGradient()
    {
        const int width = 100;
        var image = Gradient(width, 40, (x, _) => ((byte)(x * 2), (byte)(x * 2), (byte)(x * 2)));
        var expected = Gradient(width, 40, (x, _) => ((byte)(x * 2), (byte)(x * 2), (byte)(x * 2)));

        // O texto original: pixels escuros no meio de um fundo que só varia na horizontal.
        for (var y = 15; y < 25; y++)
        {
            for (var x = 30; x < 70; x++)
            {
                var at = ((y * width) + x) * 4;
                image[at] = image[at + 1] = image[at + 2] = 0;
            }
        }

        ImageEraser.Erase(image, width, 40, new ImageRegion(28, 12, 44, 16), ImageEraseModes.Surrounding);

        for (var x = 28; x < 72; x++)
        {
            var got = Pixel(image, width, x, 20);
            var want = Pixel(expected, width, x, 20);
            Assert.InRange(got.R, want.R - 4, want.R + 4);
        }
    }

    [Fact]
    public void SurroundingOnAFlatBackgroundIsExact()
    {
        var image = Gradient(30, 30, (_, _) => (60, 120, 180));
        image[((15 * 30) + 15) * 4] = 0;

        ImageEraser.Erase(image, 30, 30, new ImageRegion(10, 10, 10, 10), ImageEraseModes.Surrounding);

        Assert.Equal(((byte)60, (byte)120, (byte)180, (byte)255), Pixel(image, 30, 15, 15));
    }

    [Fact]
    public void SurroundingAtTheImageEdgeCopiesTheOnlyNeighbour()
    {
        var image = Gradient(10, 10, (_, y) => ((byte)(y * 10), 0, 0));

        ImageEraser.Erase(image, 10, 10, new ImageRegion(0, 0, 10, 4), ImageEraseModes.Surrounding);

        // Sem lado de cima nem laterais, só a borda de baixo (linha 4) serve de modelo.
        Assert.Equal((byte)40, Pixel(image, 10, 3, 1).B);
    }

    [Fact]
    public void RegionBeyondTheImageIsClipped()
    {
        var image = Gradient(10, 10, (_, _) => (1, 2, 3));

        ImageEraser.Erase(image, 10, 10, new ImageRegion(8, 8, 50, 50), ImageEraseModes.Solid, (9, 9, 9));

        Assert.Equal(((byte)9, (byte)9, (byte)9, (byte)255), Pixel(image, 10, 9, 9));
        Assert.Equal(((byte)1, (byte)2, (byte)3, (byte)255), Pixel(image, 10, 7, 7));
    }

    [Fact]
    public void RegionOutsideTheImageChangesNothing()
    {
        var image = Gradient(10, 10, (x, y) => ((byte)x, (byte)y, 0));
        var before = (byte[])image.Clone();

        ImageEraser.Erase(image, 10, 10, new ImageRegion(20, 20, 5, 5), ImageEraseModes.Surrounding);

        Assert.Equal(before, image);
    }

    [Fact]
    public void RejectsPixelsThatDoNotMatchTheSize()
    {
        Assert.Throws<ArgumentException>(() => ImageEraser.Erase(new byte[10], 10, 10, new ImageRegion(0, 0, 1, 1), ImageEraseModes.Solid));
    }

    [Fact]
    public void RejectsAnUnknownMode()
    {
        Assert.Throws<ArgumentException>(() => ImageEraser.Erase(new byte[400], 10, 10, new ImageRegion(0, 0, 1, 1), "borrar"));
    }
}
