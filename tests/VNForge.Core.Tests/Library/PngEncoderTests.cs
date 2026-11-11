using VNForge.Core.Library;

namespace VNForge.Core.Tests.Library;

public sealed class PngEncoderTests
{
    // Valores que passam de 255 na diferença entre vizinhos, para o filtro Sub dar a volta em byte.
    private static WindowImage Pattern(int width, int height)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = ((y * width) + x) * 4;
                bgra[at] = (byte)((x * 37) + y);
                bgra[at + 1] = (byte)((y * 91) ^ x);
                bgra[at + 2] = (byte)(x % 2 == 0 ? 250 : 3);
                bgra[at + 3] = 0;
            }
        }

        return new WindowImage(width, height, bgra);
    }

    [Fact]
    public void A_imagem_volta_igual_pixel_a_pixel()
    {
        var image = Pattern(37, 23);

        var (width, height, rgb) = PngTestDecoder.Decode(PngEncoder.Encode(image));

        Assert.Equal((37, 23), (width, height));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = image.PixelAt(x, y);
                var at = ((y * width) + x) * 3;
                Assert.Equal((r, g, b), (rgb[at], rgb[at + 1], rgb[at + 2]));
            }
        }
    }

    [Theory]
    [InlineData(2, 2, 15)]
    public void Imagem_com_tamanho_invalido_e_recusada(int width, int height, int length)
    {
        Assert.ThrowsAny<ArgumentException>(() => new WindowImage(width, height, new byte[length]));
    }
}
