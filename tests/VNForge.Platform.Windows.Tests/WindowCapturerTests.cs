using System.Runtime.Versioning;
using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Library;

namespace VNForge.Platform.Windows.Tests;

/// <summary>Capturam uma janela de verdade, criada pelo próprio teste, e por isso só rodam no Windows e numa sessão com área de trabalho.</summary>
[SupportedOSPlatform("windows")]
[Trait("Risco", "R7")]
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
}
