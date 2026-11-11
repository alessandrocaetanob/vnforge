using VNForge.Core.Library;

namespace VNForge.Platform.Windows.Tests;

/// <summary>
/// A escolha da janela e as regras da captura, com um sistema de janelas falso no lugar do Windows: rodam em qualquer sistema, inclusive no CircleCI (Linux).
/// O que fica de fora é só o encanamento de <c>user32</c> e <c>gdi32</c> (<see cref="NativeWindowSystem"/>), coberto pelos testes de janela real.
/// </summary>
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

    [Fact]
    public void Entre_as_janelas_que_combinam_vale_a_maior()
    {
        AddGame(1, "CLANNAD", 100, 100);
        AddGame(2, "CLANNAD", 640, 480).Pixels = Solid(640, 480, 9);
        AddGame(3, "CLANNAD", 300, 300);
        BringToFront(2);

        var image = Capturer.Capture(new WindowTarget(TitleContains: "clannad"));

        Assert.Equal((640, 480), (image.Width, image.Height));
    }

    [Fact]
    public void Janela_invisivel_ou_com_dona_nao_e_candidata()
    {
        AddGame(1, "CLANNAD", 900, 900).Visible = false;
        AddGame(2, "CLANNAD", 800, 800).HasOwner = true;
        AddGame(3, "CLANNAD", 100, 100);
        BringToFront(3);

        var image = Capturer.Capture(new WindowTarget(TitleContains: "CLANNAD"));

        Assert.Equal((100, 100), (image.Width, image.Height));
    }

    [Fact]
    public void O_titulo_ignora_maiusculas_e_a_pasta_exige_o_programa_dentro_dela()
    {
        AddGame(1, "Outro jogo", 500, 500).Program = Path.Combine(Path.GetTempPath(), "outra-pasta", "x.exe");
        AddGame(2, "Clannad HD", 200, 200);
        AddGame(3, "Navegador", 700, 700).Program = null;
        BringToFront(2);

        var byFolder = Capturer.Capture(new WindowTarget(Folder: GameFolder + Path.DirectorySeparatorChar));
        var byFolderAndTitle = Capturer.Capture(new WindowTarget(Folder: GameFolder, TitleContains: "CLANNAD hd"));

        Assert.Equal(200, byFolder.Width);
        Assert.Equal(200, byFolderAndTitle.Width);
    }

    [Theory]
    [InlineData("pasta", "titulo", "com o título 'titulo' e de um programa da pasta 'pasta'")]
    [InlineData("pasta", null, "de um programa da pasta 'pasta'")]
    [InlineData(null, "titulo", "com o título 'titulo'")]
    public void Sem_janela_a_mensagem_diz_o_que_foi_procurado(string? folder, string? title, string expected)
    {
        var error = Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Folder: folder, TitleContains: title)));

        Assert.Contains(expected, error.Message);
    }

    // ---- a janela escolhida ----

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    public void Janela_sem_area_nao_captura(int width, int height)
    {
        AddGame(width: width, height: height);

        var error = Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Handle: Game)));

        Assert.Contains("não tem área", error.Message);
    }

    [Fact]
    public void Janela_maior_que_o_limite_e_recusada()
    {
        AddGame(width: WindowImage.MaxSide + 1, height: 10);

        var error = Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Handle: Game)));

        Assert.Contains("maior que o limite", error.Message);
    }

    // ---- de onde vem a imagem ----

    [Fact]
    public void Sem_janela_em_primeiro_plano_ou_fora_de_qualquer_janela_nao_copia_a_tela()
    {
        AddGame(width: 4, height: 2);

        // Nenhuma janela em primeiro plano.
        _system.ForegroundWindow = 0;
        Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Handle: Game)));

        // O ponto não cai em janela nenhuma.
        BringToFront(Game);
        _system.WindowAtOverride = (_, _) => 0;
        Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Handle: Game)));

        // O sistema não converte o ponto para a tela.
        _system.WindowAtOverride = null;
        _system.ClientToScreenFails = true;
        Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Handle: Game)));

        Assert.Equal(0, _system.ScreenCopies);
    }

    [Fact]
    public void Se_a_copia_da_tela_falha_vale_a_imagem_preta_da_janela_que_atendeu()
    {
        AddGame(width: 4, height: 2);
        BringToFront(Game);
        _system.Screen = null;

        var image = Capturer.Capture(new WindowTarget(Handle: Game));

        Assert.True(WindowCapturer.IsBlank(image.Bgra));
    }

    [Fact]
    public void Se_a_janela_nao_atendeu_e_a_copia_da_tela_falha_nao_ha_imagem()
    {
        AddGame(width: 4, height: 2).PrintSucceeds = false;
        BringToFront(Game);
        _system.Screen = null;

        var error = Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Handle: Game)));

        Assert.Contains("não conseguiu copiar", error.Message);
    }

    [Fact]
    public void A_captura_devolve_a_percepcao_de_dpi_no_fim()
    {
        AddGame(width: 4, height: 2).Pixels = Solid(4, 2, 1);

        Capturer.Capture(new WindowTarget(Handle: Game));
        Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Handle: 99)));

        Assert.Equal((2, 2), (_system.DpiEntered, _system.DpiLeft));
    }

    // ---- imagem em branco ----

    [Fact]
    public void Imagem_toda_preta_e_branco_e_qualquer_cor_ou_o_alfa_sozinho_nao_mudam_isso()
    {
        Assert.True(WindowCapturer.IsBlank([]));
        Assert.True(WindowCapturer.IsBlank([0, 0, 0, 0, 0, 0, 0, 255]));
        Assert.False(WindowCapturer.IsBlank([0, 0, 0, 0, 0, 0, 1, 0]));
        Assert.False(WindowCapturer.IsBlank([0, 0, 0, 0, 0, 1, 0, 0]));
        Assert.False(WindowCapturer.IsBlank([1, 0, 0, 0]));
    }

    [Fact]
    public void Fora_do_Windows_o_capturador_de_verdade_diz_que_so_funciona_la()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "No Windows o capturador de verdade captura de fato.");

        var error = Assert.Throws<LibraryException>(() => new WindowCapturer().Capture(new WindowTarget(Handle: 1)));

        Assert.Contains("só funciona no Windows", error.Message);
    }

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
