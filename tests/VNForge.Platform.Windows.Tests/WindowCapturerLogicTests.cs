using VNForge.Core.Library;

namespace VNForge.Platform.Windows.Tests;

/// <summary>
/// A escolha da janela e as regras da captura, com um sistema de janelas falso no lugar do Windows: rodam em qualquer sistema, inclusive no CircleCI (Linux).
/// O que fica de fora é só o encanamento de <c>user32</c> e <c>gdi32</c> (<see cref="NativeWindowSystem"/>), coberto pelos testes de janela real.
/// </summary>
[Trait("Risco", "R7")]
public sealed class WindowCapturerLogicTests
{
    private const nint Game = 10;
    private const nint Other = 20;

    private readonly FakeWindowSystem _system = new();

    private WindowCapturer Capturer => new(_system);

    private static string GameFolder => Path.Combine(Path.GetTempPath(), "vnforge-jogo");

    private FakeWindow AddGame(nint handle = Game, string title = "CLANNAD", int width = 800, int height = 600)
    {
        var window = new FakeWindow { Title = title, Program = Path.Combine(GameFolder, "game.exe"), Width = width, Height = height };
        _system.Windows[handle] = window;
        return window;
    }

    private void BringToFront(nint handle) => _system.ForegroundWindow = handle;

    // ---- escolha da janela ----

    // ---- a janela escolhida ----

    [Fact]
    public void Janela_maior_que_o_limite_e_recusada()
    {
        AddGame(width: WindowImage.MaxSide + 1, height: 10);

        var error = Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Handle: Game)));

        Assert.Contains("maior que o limite", error.Message);
    }

    // ---- de onde vem a imagem ----

    [Fact]
    public void Se_a_janela_nao_atendeu_e_a_copia_da_tela_falha_nao_ha_imagem()
    {
        AddGame(width: 4, height: 2).PrintSucceeds = false;
        BringToFront(Game);
        _system.Screen = null;

        var error = Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Handle: Game)));

        Assert.Contains("não conseguiu copiar", error.Message);
    }

    // ---- imagem em branco ----

    private static byte[] Solid(int width, int height, byte blue)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = blue;
        }

        return bgra;
    }

    private sealed class FakeWindow
    {
        public string Title { get; set; } = string.Empty;

        public string? Program { get; set; }

        public bool Visible { get; set; } = true;

        public bool HasOwner { get; set; }

        public bool Minimized { get; set; }

        public bool ClientSizeKnown { get; set; } = true;

        public int Width { get; set; }

        public int Height { get; set; }

        public nint? Root { get; set; }

        public bool PrintSucceeds { get; set; } = true;

        public byte[]? Pixels { get; set; }
    }

    private sealed class FakeWindowSystem : IWindowSystem
    {
        public Dictionary<nint, FakeWindow> Windows { get; } = [];

        public nint ForegroundWindow { get; set; }

        /// <summary>Quem está num ponto da tela; sem isto, a janela de primeiro plano.</summary>
        public Func<int, int, nint>? WindowAtOverride { get; set; }

        public bool ClientToScreenFails { get; set; }

        /// <summary>O que a cópia da tela devolve; nulo é falha.</summary>
        public byte[]? Screen { get; set; } = [];

        public int ScreenCopies { get; private set; }

        public int DpiEntered { get; private set; }

        public int DpiLeft { get; private set; }

        public IReadOnlyList<nint> TopLevelWindows() => [.. Windows.Keys];

        public bool Exists(nint window) => Windows.ContainsKey(window);

        public bool IsVisible(nint window) => Windows[window].Visible;

        public bool IsMinimized(nint window) => Windows[window].Minimized;

        public bool HasOwner(nint window) => Windows[window].HasOwner;

        public string TitleOf(nint window) => Windows[window].Title;

        public string? ProgramOf(nint window) => Windows[window].Program;

        public bool TryGetClientSize(nint window, out int width, out int height)
        {
            (width, height) = (Windows[window].Width, Windows[window].Height);
            return Windows[window].ClientSizeKnown;
        }

        public nint Foreground() => ForegroundWindow;

        public nint RootOf(nint window) => Windows.TryGetValue(window, out var found) ? found.Root ?? window : 0;

        public nint WindowAt(int screenX, int screenY) => WindowAtOverride?.Invoke(screenX, screenY) ?? ForegroundWindow;

        public bool TryClientToScreen(nint window, int x, int y, out int screenX, out int screenY)
        {
            (screenX, screenY) = (x + 100, y + 100);
            return !ClientToScreenFails;
        }

        public PrintedWindow Print(nint window, int width, int height)
        {
            var found = Windows[window];
            return new PrintedWindow(found.PrintSucceeds, found.Pixels ?? new byte[width * height * 4]);
        }

        public byte[]? CopyFromScreen(nint window, int width, int height)
        {
            ScreenCopies++;
            return Screen is { Length: 0 } ? new byte[width * height * 4] : Screen;
        }

        public IDisposable EnterDpiAwareness()
        {
            DpiEntered++;
            return new Leave(this);
        }

        private sealed class Leave(FakeWindowSystem owner) : IDisposable
        {
            public void Dispose() => owner.DpiLeft++;
        }
    }
}
