using VNForge.Core.Images;

namespace VNForge.Core.Tests.Images;

[Trait("Risco", "R3")]
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
}
