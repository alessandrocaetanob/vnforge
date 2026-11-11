using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Styling;

using VNForge.App.Services;
using VNForge.App.Tests;
using VNForge.App.Tests.Screens;

namespace VNForge.Capturas;

internal static class Capturas
{
    private const string Usage = "uso: VNForge.Capturas [--saida pasta] [--tela nome] [--tema escuro|claro]";

    public static int Run(string[] args)
    {
        string output = Path.Combine(".tmp", "capturas");
        string? only = null;
        var themes = new List<(string Name, ThemeVariant Variant)> { ("escuro", ThemeVariant.Dark), ("claro", ThemeVariant.Light) };
        var index = 0;
        while (index < args.Length)
        {
            var value = index + 1 < args.Length ? args[index + 1] : null;
            switch (args[index])
            {
                case "--saida" when value is not null:
                    output = value;
                    break;
                case "--tela" when value is not null:
                    only = value;
                    break;
                case "--tema" when value is "escuro" or "claro":
                    themes = [themes.Single(t => t.Name == value)];
                    break;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }

            index += 2;
        }

        var names = ScreenCatalog.Pages.Select(p => p.Name).Concat(ScreenCatalog.Standalone.Select(s => s.Name)).ToList();
        if (only is not null && !names.Contains(only))
        {
            Console.Error.WriteLine($"Tela desconhecida '{only}'. As telas: {string.Join(", ", names)}.");
            return 2;
        }

        Directory.CreateDirectory(output);
        using var session = HeadlessUnitTestSession.StartNew(typeof(CapturasEntry));
        var count = 0;
        foreach (var (themeName, theme) in themes)
        {
            // O mesmo projeto do smoke (os oito scripts nomeados), numa pasta nova por tema: o segundo tema não herda o que o primeiro gravou (backups, estado, recentes).
            using var demo = new TempFolder();
            var root = session.Dispatch(() => ScreenCatalog.CreateDemoFolder(demo, trimmed: true), CancellationToken.None).GetAwaiter().GetResult();
            count += session.Dispatch(() => CaptureAsync(root, theme, themeName, output, only), CancellationToken.None).GetAwaiter().GetResult();
        }

        Console.WriteLine($"{count} capturas em {Path.GetFullPath(output)}");
        return 0;
    }

    private static async Task<int> CaptureAsync(string root, ThemeVariant theme, string themeName, string output, string? only)
    {
        var count = 0;
        var pages = ScreenCatalog.Pages.Where(p => only is null || p.Name == only).ToList();
        if (pages.Count > 0)
        {
            using var host = await ScreenHost.OpenProjectAsync(root, theme);
            foreach (var (name, key) in pages)
            {
                await host.GoToAsync(key);
                Save(host, output, name, themeName);
                count++;
            }
        }

        foreach (var screen in ScreenCatalog.Standalone.Where(s => only is null || s.Name == only))
        {
            using var host = await screen.Open(root, theme);
            Save(host, output, screen.Name, themeName);
            count++;
        }

        return count;
    }

    private static void Save(ScreenHost host, string output, string name, string themeName)
    {
        host.Settle();
        var path = Path.Combine(output, $"{name}-{themeName}.png");
        using var frame = host.Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("O Avalonia.Headless não devolveu o quadro; confira o UseSkia na sessão.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
        Console.WriteLine(path);
    }
}

/// <summary>Ponto de entrada da sessão headless: sobe o <see cref="VNForge.App.App"/> de verdade (tema, estilos e recursos), com o Skia que desenha de verdade.</summary>
public static class CapturasEntry
{
    public static AppBuilder BuildAvaloniaApp()
    {
        // O relógio das animações não anda no headless: sem isto uma página recém-trocada ficaria parada no primeiro quadro (opacidade 0).
        _ = MotionPreferences.Force(false);
        return AppBuilder.Configure<VNForge.App.App>()
            .With(new FontManagerOptions { DefaultFamilyName = "avares://VNForge.App/Assets/Fonts#Noto Sans" })
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
