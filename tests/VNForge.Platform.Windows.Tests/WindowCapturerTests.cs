using System.Runtime.Versioning;
using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Library;

namespace VNForge.Platform.Windows.Tests;

/// <summary>Capturam uma janela de verdade, criada pelo próprio teste, e por isso só rodam no Windows e numa sessão com área de trabalho.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowCapturerTests
{
    private const byte Red = 0x12;
    private const byte Green = 0x9A;
    private const byte Blue = 0x56;

    private static readonly WindowCapturer Capturer = new();

    private static void RequireWindows() => Assert.SkipUnless(OperatingSystem.IsWindows(), "A captura de janela só existe no Windows.");

    private static string OwnFolder => Path.GetDirectoryName(Environment.ProcessPath)!;

    [Fact]
    public void Lista_as_janelas_de_nivel_mais_alto_sem_repetir_nenhuma()
    {
        RequireWindows();
        using var window = new TestWindow(Red, Green, Blue);

        var windows = NativeWindows.TopLevelWindows();

        Assert.NotEmpty(windows);
        Assert.Contains(window.Handle, windows);
        Assert.Equal(windows.Count, windows.Distinct().Count());
    }

    [Fact]
    public void Captura_a_janela_pelo_identificador_com_o_tamanho_e_a_cor_dela()
    {
        RequireWindows();
        using var window = new TestWindow(Red, Green, Blue);

        var image = Capturer.Capture(new WindowTarget(Handle: window.Handle));

        Assert.Equal((TestWindow.Width, TestWindow.Height), (image.Width, image.Height));
        foreach (var (x, y) in new[] { (0, 0), (10, 10), (TestWindow.Width / 2, TestWindow.Height / 2), (TestWindow.Width - 1, TestWindow.Height - 1) })
        {
            Assert.Equal((Red, Green, Blue), image.PixelAt(x, y));
        }
    }

    [Fact]
    public void Acha_a_janela_pelo_titulo_e_pela_pasta_do_programa_dela()
    {
        RequireWindows();
        using var window = new TestWindow(Red, Green, Blue);

        var byTitle = Capturer.Capture(new WindowTarget(TitleContains: window.Title[10..].ToUpperInvariant()));
        var byFolderAndTitle = Capturer.Capture(new WindowTarget(Folder: OwnFolder, TitleContains: window.Title));

        Assert.Equal((Red, Green, Blue), byTitle.PixelAt(5, 5));
        Assert.Equal((Red, Green, Blue), byFolderAndTitle.PixelAt(5, 5));
        Assert.Equal((TestWindow.Width, TestWindow.Height), (byFolderAndTitle.Width, byFolderAndTitle.Height));
    }

    [Fact]
    public void Janela_de_outra_pasta_nao_combina()
    {
        RequireWindows();
        using var window = new TestWindow(Red, Green, Blue);
        var elsewhere = Path.Combine(Path.GetTempPath(), "vnforge-sem-jogo-" + Guid.NewGuid().ToString("N"));

        var error = Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Folder: elsewhere, TitleContains: window.Title)));

        Assert.Contains("Não achei nenhuma janela", error.Message);
    }

    [Fact]
    public void Janela_minimizada_diz_que_esta_minimizada()
    {
        RequireWindows();
        using var window = new TestWindow(Red, Green, Blue);
        window.Minimize();

        var byHandle = Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Handle: window.Handle)));
        var byTitle = Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(TitleContains: window.Title)));

        Assert.Contains("minimizada", byHandle.Message);
        Assert.Contains("minimizada", byTitle.Message);
    }

    [Fact]
    public void Janela_que_sumiu_e_um_erro_claro()
    {
        RequireWindows();
        nint handle;
        using (var window = new TestWindow(Red, Green, Blue))
        {
            handle = window.Handle;
        }

        var error = Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Handle: handle)));

        Assert.Contains("não existe mais", error.Message);
    }

    [Fact]
    public void Imagem_preta_de_janela_coberta_por_outra_nao_cai_para_a_copia_da_tela()
    {
        RequireWindows();
        // Preta, o PrintWindow não distingue de "o jogo não deixou copiar": cai para a cópia da tela, que traria a janela vermelha de cima.
        using var game = new TestWindow(0, 0, 0);
        using var covering = new TestWindow(Red, Green, Blue);

        var error = Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Handle: game.Handle)));

        Assert.Contains("Traga o jogo para a frente", error.Message);
    }

    [Fact]
    public void Sem_dizer_qual_janela_nao_captura_nada()
    {
        RequireWindows();

        Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget()));
    }

    [Fact]
    public void A_captura_de_uma_janela_de_teste_e_gravada_com_a_data()
    {
        RequireWindows();
        var folder = Path.Combine(Path.GetTempPath(), "vnforge-captura-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 9, 23, 30, 15, TimeSpan.Zero));
            clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT"));
            var store = new GameCapturesFile(folder, clock);
            using var window = new TestWindow(Red, Green, Blue);

            var capture = store.Save("g0123abcd", Capturer.Capture(new WindowTarget(Handle: window.Handle)));

            Assert.Equal("20261109-203015.png", capture.File);
            var png = store.PathOf("g0123abcd", capture);
            Assert.StartsWith(folder, png);
            var bytes = File.ReadAllBytes(png);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
            Assert.Equal([capture], store.Load().Of("g0123abcd"));
            Assert.Equal((TestWindow.Width, TestWindow.Height), (capture.Width, capture.Height));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
