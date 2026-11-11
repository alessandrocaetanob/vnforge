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
    public void Imagem_preta_de_janela_coberta_por_outra_nao_cai_para_a_copia_da_tela()
    {
        RequireWindows();
        // Preta, o PrintWindow não distingue de "o jogo não deixou copiar": cai para a cópia da tela, que traria a janela vermelha de cima.
        using var game = new TestWindow(0, 0, 0);
        using var covering = new TestWindow(Red, Green, Blue);

        var error = Assert.Throws<LibraryException>(() => Capturer.Capture(new WindowTarget(Handle: game.Handle)));

        Assert.Contains("Traga o jogo para a frente", error.Message);
    }
}
