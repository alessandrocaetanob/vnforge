using System.Runtime.Versioning;
using VNForge.Core.Library;

namespace VNForge.Platform.Windows;

/// <summary>
/// O <see cref="IWindowCapturer"/> de verdade: acha a janela e copia a área do jogo (sem a barra de título). Primeiro pede a imagem à própria janela
/// (<c>PrintWindow</c> com <c>PW_RENDERFULLCONTENT</c>), que não depende de ela estar à frente de outras. Se falhar ou vier toda preta, como acontece com alguns jogos de DirectX,
/// copia da tela o mesmo retângulo, mas só quando a janela é a de primeiro plano e nada a cobre; do contrário a captura falha e pede o jogo à frente, porque a cópia
/// traria o conteúdo de outro programa. Jogo em tela cheia exclusiva pode sair preto nos dois caminhos. As chamadas ao Windows ficam em <see cref="IWindowSystem"/>.
/// </summary>
public sealed class WindowCapturer : IWindowCapturer
{
    private readonly IWindowSystem? _system;

    public WindowCapturer()
    {
    }

    internal WindowCapturer(IWindowSystem system) => _system = system;

    public WindowImage Capture(WindowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var system = _system ?? NativeSystem();
        using var dpi = system.EnterDpiAwareness();
        var window = target.Handle ?? Find(system, target);
        return Copy(system, window);
    }

    private static NativeWindowSystem NativeSystem()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new LibraryException("Capturar a janela do jogo só funciona no Windows.");
        }

        return CreateNative();
    }

    [SupportedOSPlatform("windows")]
    private static NativeWindowSystem CreateNative() => new();

    private static nint Find(IWindowSystem system, WindowTarget target)
    {
        if (target.Folder is null && target.TitleContains is null)
        {
            throw new LibraryException("Diga qual janela capturar: a pasta do jogo ou um pedaço do título dela.");
        }

        var folder = target.Folder is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.Folder)) + Path.DirectorySeparatorChar;
        nint best = 0;
        long bestArea = 0;
        var minimized = false;
        foreach (var window in system.TopLevelWindows())
        {
            if (!Matches(system, window, target.TitleContains, folder))
            {
                continue;
            }

            if (system.IsMinimized(window))
            {
                minimized = true;
            }
            else if (system.TryGetClientSize(window, out var width, out var height) && (long)width * height > bestArea)
            {
                (best, bestArea) = (window, (long)width * height);
            }
        }

        return best != 0 ? best : throw NotFound(target, minimized);
    }

    private static bool Matches(IWindowSystem system, nint window, string? titlePart, string? folder) =>
        system.IsVisible(window)
        && !system.HasOwner(window)
        && (titlePart is null || system.TitleOf(window).Contains(titlePart, StringComparison.OrdinalIgnoreCase))
        && (folder is null || system.ProgramOf(window)?.StartsWith(folder, StringComparison.OrdinalIgnoreCase) == true);

    private static LibraryException NotFound(WindowTarget target, bool minimized) =>
        new(minimized ? "A janela está minimizada. Traga-a de volta e tente de novo." : $"Não achei nenhuma janela aberta {Describe(target)}.");

    private static string Describe(WindowTarget target) =>
        (target.Folder, target.TitleContains) switch
        {
            ({ } folder, { } title) => $"com o título '{title}' e de um programa da pasta '{folder}'",
            ({ } folder, null) => $"de um programa da pasta '{folder}'",
            _ => $"com o título '{target.TitleContains}'",
        };

    /// <summary>
    /// Captura a imagem da janela. Quando o <c>PrintWindow</c> falha ou volta preto, a imagem dele fica viva enquanto a cópia da tela aloca a segunda, porque é
    /// ela o último recurso se a cópia falhar: em janelas enormes o pico de memória chega a duas imagens.
    /// </summary>
    private static WindowImage Copy(IWindowSystem system, nint window)
    {
        var (width, height) = ClientSizeOf(system, window);
        var printed = system.Print(window, width, height);
        var pixels = printed.Succeeded && !IsBlank(printed.Bgra) ? printed.Bgra : FromScreen(system, window, width, height, printed);
        return new WindowImage(width, height, pixels, new WindowSource(system.TitleOf(window), system.ProgramOf(window)));
    }

    private static (int Width, int Height) ClientSizeOf(IWindowSystem system, nint window)
    {
        if (!system.Exists(window))
        {
            throw new LibraryException("A janela não existe mais.");
        }

        if (system.IsMinimized(window))
        {
            throw new LibraryException("A janela está minimizada. Traga-a de volta e tente de novo.");
        }

        if (!system.TryGetClientSize(window, out var width, out var height) || width < 1 || height < 1)
        {
            throw new LibraryException("A janela não tem área para capturar.");
        }

        if (width > WindowImage.MaxSide || height > WindowImage.MaxSide)
        {
            throw new LibraryException($"A janela ({width}x{height}) é maior que o limite de {WindowImage.MaxSide} pixels de lado.");
        }

        return (width, height);
    }

    /// <summary>A cópia da tela traz o que estiver por cima da janela, que pode ser conteúdo de outro programa: só vale com a janela na frente.</summary>
    private static byte[] FromScreen(IWindowSystem system, nint window, int width, int height, PrintedWindow printed)
    {
        if (!IsInFront(system, window, width, height))
        {
            throw new LibraryException("Não consegui a imagem da janela por ela mesma e outra janela está na frente do jogo. Traga o jogo para a frente e tente de novo.");
        }

        var screen = system.CopyFromScreen(window, width, height);
        if (screen is not null)
        {
            return screen;
        }

        // O PrintWindow deu uma imagem (preta, mas dele): melhor ela do que nada. Sem imagem nenhuma, não há o que devolver.
        return printed.Succeeded ? printed.Bgra : throw new LibraryException("O Windows não conseguiu copiar a imagem da janela.");
    }

    /// <summary>
    /// Vale para copiar da tela: a janela é a de primeiro plano e o centro e os quatro cantos da área do jogo, a um pixel da borda, caem nela e não em outra janela.
    /// Ponto fora de qualquer janela ou da tela também reprova.
    /// </summary>
    internal static bool IsInFront(IWindowSystem system, nint window, int width, int height)
    {
        var root = system.RootOf(window);
        var foreground = system.Foreground();
        if (root == 0 || foreground == 0 || system.RootOf(foreground) != root)
        {
            return false;
        }

        (int X, int Y)[] probes = [(1, 1), (width - 2, 1), (1, height - 2), (width - 2, height - 2), (width / 2, height / 2)];
        return probes.All(probe => IsOnTop(system, window, root, probe.X, probe.Y));
    }

    private static bool IsOnTop(IWindowSystem system, nint window, nint root, int x, int y)
    {
        if (!system.TryClientToScreen(window, x, y, out var screenX, out var screenY))
        {
            return false;
        }

        var top = system.WindowAt(screenX, screenY);
        return top != 0 && system.RootOf(top) == root;
    }

    internal static bool IsBlank(byte[] bgra)
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
