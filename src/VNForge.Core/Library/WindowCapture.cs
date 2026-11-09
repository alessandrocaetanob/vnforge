namespace VNForge.Core.Library;

/// <summary>Os pixels de uma janela, em <c>B, G, R, A</c> por pixel, linha a linha de cima para baixo. O alfa não vale: a captura sai opaca.</summary>
public sealed class WindowImage
{
    /// <summary>O maior lado aceito; passa de qualquer tela e mantém a conta de bytes longe do estouro.</summary>
    public const int MaxSide = 16_384;

    /// <exception cref="ArgumentOutOfRangeException">Largura ou altura fora de 1 a <see cref="MaxSide"/>, ou <paramref name="bgra"/> com tamanho diferente de largura × altura × 4.</exception>
    public WindowImage(int width, int height, byte[] bgra)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxSide);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaxSide);
        ArgumentNullException.ThrowIfNull(bgra);
        if (bgra.LongLength != (long)width * height * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(bgra), $"Uma imagem de {width}x{height} tem {(long)width * height * 4} bytes, não {bgra.LongLength}.");
        }

        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Bgra { get; }

    public (byte Red, byte Green, byte Blue) PixelAt(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        var at = ((y * Width) + x) * 4;
        return (Bgra[at + 2], Bgra[at + 1], Bgra[at]);
    }
}

/// <summary>
/// Qual janela capturar. <paramref name="Handle"/> (o HWND) vale sozinho. Sem ele, a janela é uma visível de nível mais alto que cumpre tudo o que foi dado:
/// <paramref name="Folder"/> (o programa dela está nessa pasta ou dentro dela; é a pasta do jogo) e <paramref name="TitleContains"/> (um pedaço do título, sem diferenciar
/// maiúsculas). Havendo mais de uma, vale a maior.
/// </summary>
public sealed record WindowTarget(string? Folder = null, string? TitleContains = null, nint? Handle = null);

/// <summary>Captura a imagem de uma janela. Fica atrás de uma interface para os testes trocarem por uma falsa; a de verdade está em <c>VNForge.Platform.Windows</c>.</summary>
public interface IWindowCapturer
{
    /// <exception cref="LibraryException">Não há janela que combine, ela está minimizada, ou o sistema não conseguiu copiar a imagem.</exception>
    WindowImage Capture(WindowTarget target);
}
