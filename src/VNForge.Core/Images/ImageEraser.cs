namespace VNForge.Core.Images;

/// <summary>
/// Apaga uma área de uma imagem em pixels BGRA (4 bytes por pixel, linhas seguidas, sem preenchimento), o passo que antecede o texto
/// novo no editor de imagens (#535). Trabalha sobre os bytes, sem biblioteca de imagem, então o resultado é igual em qualquer sistema.
/// </summary>
public static class ImageEraser
{
    private const int BytesPerPixel = 4;

    /// <summary>Apaga <paramref name="region"/> (cortada pelas bordas da imagem) em <paramref name="bgra"/>.</summary>
    /// <param name="bgra">Pixels da imagem, <c>width * height * 4</c> bytes; é alterado no lugar.</param>
    /// <param name="width">Largura da imagem, em pixels.</param>
    /// <param name="height">Altura da imagem, em pixels.</param>
    /// <param name="region">A área a apagar.</param>
    /// <param name="mode">Um de <see cref="ImageEraseModes"/>.</param>
    /// <param name="solid">Cor RGB da área em <see cref="ImageEraseModes.Solid"/>; ignorada no outro modo.</param>
    /// <remarks>
    /// Em <see cref="ImageEraseModes.Surrounding"/>, cada pixel da área recebe a média de duas interpolações lineares: uma entre os pixels
    /// logo à esquerda e à direita da área, na mesma linha, e outra entre os de cima e de baixo, na mesma coluna. Um lado que cai fora da
    /// imagem é ignorado. Uma área que cobre a imagem inteira não tem borda de onde copiar e vira transparente.
    /// </remarks>
    /// <exception cref="ArgumentException">O tamanho de <paramref name="bgra"/> não confere com a largura e a altura, ou o modo é desconhecido.</exception>
    public static void Erase(byte[] bgra, int width, int height, ImageRegion region, string mode, (byte R, byte G, byte B) solid = default)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        ArgumentNullException.ThrowIfNull(region);
        if (width <= 0 || height <= 0 || bgra.Length != (long)width * height * BytesPerPixel)
        {
            throw new ArgumentException("O tamanho dos pixels não confere com a largura e a altura da imagem.", nameof(bgra));
        }

        if (!ImageEraseModes.IsValid(mode))
        {
            throw new ArgumentException($"Modo de apagar desconhecido: '{mode}'.", nameof(mode));
        }

        var area = region.ClipTo(width, height);
        if (area.Width == 0 || area.Height == 0)
        {
            return;
        }

        if (mode == ImageEraseModes.Solid)
        {
            Fill(bgra, width, area, solid);
            return;
        }

        Surround(bgra, width, height, area);
    }

    private static void Fill(byte[] bgra, int width, ImageRegion area, (byte R, byte G, byte B) color)
    {
        for (var y = area.Y; y < area.Y + area.Height; y++)
        {
            for (var x = area.X; x < area.X + area.Width; x++)
            {
                var at = ((y * width) + x) * BytesPerPixel;
                bgra[at] = color.B;
                bgra[at + 1] = color.G;
                bgra[at + 2] = color.R;
                bgra[at + 3] = 255;
            }
        }
    }

    private static void Surround(byte[] bgra, int width, int height, ImageRegion area)
    {
        var x1 = area.X + area.Width;
        var y1 = area.Y + area.Height;
        var source = (byte[])bgra.Clone();
        for (var y = area.Y; y < y1; y++)
        {
            for (var x = area.X; x < x1; x++)
            {
                var horizontal = Between(source, width, (area.X - 1, y), (x1, y), area.X > 0, x1 < width, (x - area.X + 0.5) / area.Width);
                var vertical = Between(source, width, (x, area.Y - 1), (x, y1), area.Y > 0, y1 < height, (y - area.Y + 0.5) / area.Height);
                var at = ((y * width) + x) * BytesPerPixel;
                for (var c = 0; c < BytesPerPixel; c++)
                {
                    bgra[at + c] = (horizontal, vertical) switch
                    {
                        ({ } h, { } v) => (byte)Math.Round((h[c] + v[c]) / 2.0),
                        ({ } h, null) => (byte)Math.Round(h[c]),
                        (null, { } v) => (byte)Math.Round(v[c]),
                        _ => 0,
                    };
                }
            }
        }
    }

    // Interpola entre o pixel antes da área e o depois; com um lado só, copia esse lado; sem nenhum, não há resposta.
    private static double[]? Between(byte[] source, int width, (int X, int Y) a, (int X, int Y) b, bool hasA, bool hasB, double t)
    {
        double[] Read((int X, int Y) p)
        {
            var at = ((p.Y * width) + p.X) * BytesPerPixel;
            return [source[at], source[at + 1], source[at + 2], source[at + 3]];
        }

        if (hasA && hasB)
        {
            var first = Read(a);
            var second = Read(b);
            return [.. first.Zip(second, (p, q) => p + ((q - p) * t))];
        }

        return hasA ? Read(a) : hasB ? Read(b) : null;
    }
}
