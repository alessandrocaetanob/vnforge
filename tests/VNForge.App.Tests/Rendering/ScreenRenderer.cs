using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Renderiza uma tela (ou qualquer controle) para PNG pelo Avalonia.Headless com Skia, para pôr ao lado do artboard nos PRs de tela.
/// A janela tem o tamanho do canvas do design, 1440×1000, e o fundo é o <c>bg-layer</c> do tema pedido.
/// </summary>
internal static class ScreenRenderer
{
    public const int Width = 1440;
    public const int Height = 1000;

    public static readonly ThemeVariant[] Themes = [ThemeVariant.Dark, ThemeVariant.Light];

    /// <summary>Nome do arquivo no formato <c>tela-escuro.png</c>.</summary>
    public static string FileName(string screen, ThemeVariant theme) =>
        $"{screen}-{(theme == ThemeVariant.Light ? "claro" : "escuro")}.png";

    /// <summary>Monta a janela, deixa o layout assentar e devolve o quadro renderizado. Chamar na thread da sessão headless.</summary>
    public static void Render(Control content, ThemeVariant theme, string path, int width = Width, int height = Height)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            RequestedThemeVariant = theme,
            Content = content,
        };
        window.Bind(
            TemplatedControl_BackgroundProperty,
            window.GetResourceObservable("Vnf.BgLayer"));
        try
        {
            window.Show();
            Settle(window);
            Save(window, path);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Salva o quadro atual de uma janela já aberta.</summary>
    public static void Save(Window window, string path)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("O Avalonia.Headless não devolveu o quadro; confira o UseSkia na sessão.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }

    public static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static readonly AvaloniaProperty<IBrush?> TemplatedControl_BackgroundProperty = Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty;
}
