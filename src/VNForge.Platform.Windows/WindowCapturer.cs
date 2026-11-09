using System.Runtime.Versioning;
using VNForge.Core.Library;

namespace VNForge.Platform.Windows;

/// <summary>
/// O <see cref="IWindowCapturer"/> de verdade: acha a janela e copia a área do jogo (sem a barra de título). Primeiro pede a imagem à própria janela
/// (<c>PrintWindow</c> com <c>PW_RENDERFULLCONTENT</c>), que não depende de ela estar à frente de outras. Se vier toda preta, como acontece com alguns jogos de DirectX,
/// copia da tela o mesmo retângulo, mas só quando a janela é a de primeiro plano e nada a cobre; do contrário a captura falha e pede o jogo à frente, porque a cópia traria o conteúdo de outro programa. Jogo em tela cheia exclusiva pode sair preto nos dois caminhos.
/// </summary>
public sealed class WindowCapturer : IWindowCapturer
{
    private const uint OwnerWindow = 4;
    private const uint RootAncestor = 2;

    public WindowImage Capture(WindowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!OperatingSystem.IsWindows())
        {
            throw new LibraryException("Capturar a janela do jogo só funciona no Windows.");
        }

        return CaptureOnWindows(target);
    }

    [SupportedOSPlatform("windows")]
    private static WindowImage CaptureOnWindows(WindowTarget target)
    {
        // Sem isto, num PC com a tela ampliada, o processo "não ciente de DPI" recebe o tamanho e a imagem em pixels virtuais.
        var previous = NativeWindows.SetThreadDpiAwarenessContext(NativeWindows.PerMonitorAwareV2);
        try
        {
            var window = target.Handle ?? Find(target);
            return Copy(window);
        }
        finally
        {
            if (previous != 0)
            {
                NativeWindows.SetThreadDpiAwarenessContext(previous);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static nint Find(WindowTarget target)
    {
        if (target.Folder is null && target.TitleContains is null)
        {
            throw new LibraryException("Diga qual janela capturar: a pasta do jogo ou um pedaço do título dela.");
        }

        var folder = target.Folder is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.Folder)) + Path.DirectorySeparatorChar;
        nint best = 0;
        long bestArea = 0;
        var minimized = false;
        foreach (var window in NativeWindows.TopLevelWindows())
        {
            if (!NativeWindows.IsWindowVisible(window) || NativeWindows.GetWindow(window, OwnerWindow) != 0)
            {
                continue;
            }

            if (target.TitleContains is { } part && !NativeWindows.TitleOf(window).Contains(part, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (folder is not null && NativeWindows.ProgramOf(window)?.StartsWith(folder, StringComparison.OrdinalIgnoreCase) != true)
            {
                continue;
            }

            if (NativeWindows.IsIconic(window))
            {
                minimized = true;
                continue;
            }

            if (!NativeWindows.GetClientRect(window, out var rect))
            {
                continue;
            }

            var area = (long)rect.Right * rect.Bottom;
            if (area > bestArea)
            {
                best = window;
                bestArea = area;
            }
        }

        if (best != 0)
        {
            return best;
        }

        throw new LibraryException(minimized
            ? "A janela está minimizada. Traga-a de volta e tente de novo."
            : $"Não achei nenhuma janela aberta {Describe(target)}.");
    }

    private static string Describe(WindowTarget target) =>
        (target.Folder, target.TitleContains) switch
        {
            ({ } folder, { } title) => $"com o título '{title}' e de um programa da pasta '{folder}'",
            ({ } folder, null) => $"de um programa da pasta '{folder}'",
            (null, { } title) => $"com o título '{title}'",
            _ => "que combine",
        };

    [SupportedOSPlatform("windows")]
    private static unsafe WindowImage Copy(nint window)
    {
        if (!NativeWindows.IsWindow(window))
        {
            throw new LibraryException("A janela não existe mais.");
        }

        if (NativeWindows.IsIconic(window))
        {
            throw new LibraryException("A janela está minimizada. Traga-a de volta e tente de novo.");
        }

        if (!NativeWindows.GetClientRect(window, out var rect) || rect.Right < 1 || rect.Bottom < 1)
        {
            throw new LibraryException("A janela não tem área para capturar.");
        }

        int width = rect.Right, height = rect.Bottom;
        if (width > WindowImage.MaxSide || height > WindowImage.MaxSide)
        {
            throw new LibraryException($"A janela ({width}x{height}) é maior que o limite de {WindowImage.MaxSide} pixels de lado.");
        }

        var screen = NativeWindows.GetDC(0);
        if (screen == 0)
        {
            throw new LibraryException("O Windows não deu acesso à tela para a captura.");
        }

        var memory = NativeWindows.CreateCompatibleDC(screen);
        nint bitmap = 0;
        nint previous = 0;
        try
        {
            if (memory == 0)
            {
                throw new LibraryException("O Windows não criou a área de cópia da captura.");
            }

            var info = new NativeWindows.BitmapInfoHeader { Size = (uint)sizeof(NativeWindows.BitmapInfoHeader), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            bitmap = NativeWindows.CreateDIBSection(screen, &info, 0, out var bits, 0, 0);
            if (bitmap == 0 || bits == null)
            {
                throw new LibraryException($"O Windows não conseguiu reservar a imagem de {width}x{height} da captura.");
            }

            previous = NativeWindows.SelectObject(memory, bitmap);
            var pixels = new byte[width * height * 4];
            var fromWindow = NativeWindows.PrintWindow(window, memory, NativeWindows.PrintClientRenderFull);
            new ReadOnlySpan<byte>(bits, pixels.Length).CopyTo(pixels);

            if (!fromWindow || IsBlank(pixels))
            {
                // A cópia da tela traz o que estiver por cima da janela, que pode ser conteúdo de outro programa.
                if (!IsInFront(window, width, height))
                {
                    throw new LibraryException("Não consegui a imagem da janela por ela mesma e outra janela está na frente do jogo. Traga o jogo para a frente e tente de novo.");
                }

                var origin = new NativeWindows.Point();
                if (NativeWindows.ClientToScreen(window, ref origin) && NativeWindows.BitBlt(memory, 0, 0, width, height, screen, origin.X, origin.Y, NativeWindows.ScreenCopy))
                {
                    new ReadOnlySpan<byte>(bits, pixels.Length).CopyTo(pixels);
                }
                else if (!fromWindow)
                {
                    throw new LibraryException("O Windows não conseguiu copiar a imagem da janela.");
                }
            }

            return new WindowImage(width, height, pixels, new WindowSource(NativeWindows.TitleOf(window), NativeWindows.ProgramOf(window)));
        }
        finally
        {
            if (previous != 0)
            {
                NativeWindows.SelectObject(memory, previous);
            }

            if (bitmap != 0)
            {
                NativeWindows.DeleteObject(bitmap);
            }

            if (memory != 0)
            {
                NativeWindows.DeleteDC(memory);
            }

            NativeWindows.ReleaseDC(0, screen);
        }
    }

    /// <summary>
    /// Vale para copiar da tela: a janela é a de primeiro plano e o centro e os quatro cantos da área do jogo, a um pixel da borda, caem nela e não em outra janela.
    /// Ponto fora de qualquer janela ou da tela também reprova.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool IsInFront(nint window, int width, int height)
    {
        var root = NativeWindows.GetAncestor(window, RootAncestor);
        var foreground = NativeWindows.GetForegroundWindow();
        if (root == 0 || foreground == 0 || NativeWindows.GetAncestor(foreground, RootAncestor) != root)
        {
            return false;
        }

        (int X, int Y)[] probes = [(1, 1), (width - 2, 1), (1, height - 2), (width - 2, height - 2), (width / 2, height / 2)];
        foreach (var (x, y) in probes)
        {
            var point = new NativeWindows.Point { X = x, Y = y };
            if (!NativeWindows.ClientToScreen(window, ref point))
            {
                return false;
            }

            var top = NativeWindows.WindowFromPoint(point);
            if (top == 0 || NativeWindows.GetAncestor(top, RootAncestor) != root)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBlank(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            if ((bgra[i] | bgra[i + 1] | bgra[i + 2]) != 0)
            {
                return false;
            }
        }

        return true;
    }
}
