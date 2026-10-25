using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Os pixels de um controle já renderizado numa janela, para conferir o desenho (a cor da tinta numa linha, o traço, o triângulo) em vez de só o tamanho.
/// As coordenadas são relativas ao canto do controle.
/// </summary>
internal sealed class PixelProbe
{
    private static readonly double[] StepFractions = [1.0, 0.75, 0.5, 0.25];

    private readonly Color[,] pixels;

    private PixelProbe(Color[,] pixels)
    {
        this.pixels = pixels;
        Width = pixels.GetLength(0);
        Height = pixels.GetLength(1);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Copia o quadro atual da janela na área do controle. Chamar na thread da sessão headless, com o layout assentado.</summary>
    public static PixelProbe Capture(Window window, Control control)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("O Avalonia.Headless não devolveu o quadro; confira o UseSkia na sessão.");
        var origin = control.TranslatePoint(default, window) ?? throw new InvalidOperationException("O controle não está na janela.");
        var width = (int)Math.Ceiling(control.Bounds.Width);
        var height = (int)Math.Ceiling(control.Bounds.Height);
        var pixels = new Color[width, height];
        using var buffer = frame.Lock();
        var bytes = new byte[4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = new PixelPoint((int)Math.Round(origin.X) + x, (int)Math.Round(origin.Y) + y);
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address + (at.Y * buffer.RowBytes) + (at.X * 4), bytes, 0, 4);
                pixels[x, y] = buffer.Format == PixelFormat.Bgra8888
                    ? Color.FromArgb(255, bytes[2], bytes[1], bytes[0])
                    : Color.FromArgb(255, bytes[0], bytes[1], bytes[2]);
            }
        }

        return new PixelProbe(pixels);
    }

    public Color At(int x, int y) => pixels[x, y];

    /// <summary>Verdadeiro quando o pixel está a no máximo <paramref name="tolerance"/> de <paramref name="color"/> em cada canal.</summary>
    public bool Near(int x, int y, Color color, int tolerance) => Distance(pixels[x, y], color) <= tolerance;

    /// <summary>Quantos pixels da linha <paramref name="y"/>, entre <paramref name="fromX"/> e <paramref name="toX"/>, têm a cor.</summary>
    public int CountInRow(int y, Color color, int tolerance, int fromX, int toX)
    {
        var count = 0;
        for (var x = fromX; x < toX; x++)
        {
            count += Near(x, y, color, tolerance) ? 1 : 0;
        }

        return count;
    }

    /// <summary>Como <see cref="CountInRow(int, Color, int, int, int)"/>, contando o pixel que bate com qualquer uma das cores (um traço de 1 pixel cai entre duas linhas quando a posição é fracionária).</summary>
    public int CountInRow(int y, IReadOnlyList<Color> colors, int tolerance, int fromX, int toX)
    {
        var count = 0;
        for (var x = fromX; x < toX; x++)
        {
            count += colors.Any(color => Near(x, y, color, tolerance)) ? 1 : 0;
        }

        return count;
    }

    /// <summary>Faixas de linhas consecutivas (de <paramref name="fromY"/> para baixo) que têm ao menos um pixel da cor entre <paramref name="fromX"/> e <paramref name="toX"/>.</summary>
    public IReadOnlyList<(int Start, int End)> RowBands(Color color, int tolerance, int fromX, int toX, int fromY = 0)
    {
        var bands = new List<(int, int)>();
        int? start = null;
        for (var y = fromY; y < Height; y++)
        {
            var hit = CountInRow(y, color, tolerance, fromX, toX) > 0;
            if (hit && start is null)
            {
                start = y;
            }
            else if (!hit && start is { } begin)
            {
                bands.Add((begin, y - 1));
                start = null;
            }
        }

        if (start is { } last)
        {
            bands.Add((last, Height - 1));
        }

        return bands;
    }

    /// <summary>Trechos verticais da coluna <paramref name="x"/> com a cor, de pelo menos <paramref name="minLength"/> pixels.</summary>
    public IReadOnlyList<(int Start, int End)> ColumnRuns(int x, Color color, int tolerance, int minLength = 3)
    {
        var runs = new List<(int, int)>();
        int? start = null;
        for (var y = 0; y <= Height; y++)
        {
            var hit = y < Height && Near(x, y, color, tolerance);
            if (hit && start is null)
            {
                start = y;
            }
            else if (!hit && start is { } begin)
            {
                if (y - begin >= minLength)
                {
                    runs.Add((begin, y - 1));
                }

                start = null;
            }
        }

        return runs;
    }

    /// <summary>Os mesmos pixels nos dois quadros.</summary>
    public bool SameAs(PixelProbe other)
    {
        if (Width != other.Width || Height != other.Height)
        {
            return false;
        }

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (pixels[x, y] != other.pixels[x, y])
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary><paramref name="ink"/> com a opacidade dada sobre <paramref name="background"/>, como o desenho a compõe.</summary>
    public static Color Blend(Color ink, double opacity, Color background) => Color.FromArgb(
        255,
        (byte)Math.Round((ink.R * opacity) + (background.R * (1 - opacity))),
        (byte)Math.Round((ink.G * opacity) + (background.G * (1 - opacity))),
        (byte)Math.Round((ink.B * opacity) + (background.B * (1 - opacity))));

    /// <summary>A cor com 100%, 75%, 50% e 25% da opacidade: o mesmo traço quando cai inteiro numa linha de pixels ou dividido entre duas.</summary>
    public static IReadOnlyList<Color> Steps(Color ink, double opacity, Color background) =>
        [.. StepFractions.Select(fraction => Blend(ink, opacity * fraction, background))];

    /// <summary>Quantos pixels da área têm uma das cores. A área é cortada nas bordas do controle.</summary>
    public int CountInArea(Rect area, IReadOnlyList<Color> colors, int tolerance)
    {
        var count = 0;
        for (var y = Math.Max(0, (int)Math.Floor(area.Top)); y < Math.Min(Height, (int)Math.Ceiling(area.Bottom)); y++)
        {
            count += CountInRow(y, colors, tolerance, Math.Max(0, (int)Math.Floor(area.Left)), Math.Min(Width, (int)Math.Ceiling(area.Right)));
        }

        return count;
    }

    /// <summary>Quantos pixels da área têm a cor.</summary>
    public int CountInArea(Rect area, Color color, int tolerance) => CountInArea(area, [color], tolerance);

    private static int Distance(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
}
