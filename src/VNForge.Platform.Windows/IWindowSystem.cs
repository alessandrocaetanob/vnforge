namespace VNForge.Platform.Windows;

/// <summary>O que <see cref="WindowCapturer"/> precisa saber do sistema de janelas. Fica atrás de uma interface para a escolha da janela e as regras da captura rodarem em teste, em qualquer sistema, com um falso.</summary>
internal interface IWindowSystem
{
    /// <summary>As janelas de nível mais alto, da que está na frente para a de trás.</summary>
    IReadOnlyList<nint> TopLevelWindows();

    bool Exists(nint window);

    bool IsVisible(nint window);

    bool IsMinimized(nint window);

    /// <summary>Janela com dona é caixa de diálogo ou dica, não a janela principal.</summary>
    bool HasOwner(nint window);

    string TitleOf(nint window);

    /// <summary>O caminho do programa dono da janela; nulo quando o sistema não deixa ler.</summary>
    string? ProgramOf(nint window);

    /// <summary>O tamanho da área do jogo (sem a barra de título); falso quando o sistema não informa.</summary>
    bool TryGetClientSize(nint window, out int width, out int height);

    /// <summary>A janela de primeiro plano, ou zero.</summary>
    nint Foreground();

    /// <summary>A janela de nível mais alto a que a janela pertence (ela mesma, se já for).</summary>
    nint RootOf(nint window);

    /// <summary>A janela visível mais à frente naquele ponto da tela, ou zero.</summary>
    nint WindowAt(int screenX, int screenY);

    bool TryClientToScreen(nint window, int x, int y, out int screenX, out int screenY);

    /// <summary>A imagem que a própria janela devolve (<c>PrintWindow</c>), em <c>B, G, R, A</c>. <c>Succeeded</c> é falso quando a janela não a deu.</summary>
    /// <exception cref="VNForge.Core.Library.LibraryException">O sistema não conseguiu reservar a área de cópia.</exception>
    PrintedWindow Print(nint window, int width, int height);

    /// <summary>A imagem da tela no retângulo da janela, ou nulo quando o sistema não copiou. Traz o que estiver por cima da janela.</summary>
    byte[]? CopyFromScreen(nint window, int width, int height);

    /// <summary>Põe a thread ciente de DPI por monitor até o fim do escopo, para o tamanho vir em pixels reais e não ampliado pela escala da tela.</summary>
    IDisposable EnterDpiAwareness();
}

/// <summary>O resultado do <c>PrintWindow</c>: se a janela atendeu, e os pixels (que existem, zerados, mesmo quando ela não atendeu).</summary>
internal readonly record struct PrintedWindow(bool Succeeded, byte[] Bgra);
