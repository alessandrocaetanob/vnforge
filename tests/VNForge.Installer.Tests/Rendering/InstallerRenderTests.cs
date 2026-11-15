using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using VNForge.Installer.ViewModels;
using VNForge.Installer.Views;
using VNForge.Patching;

namespace VNForge.Installer.Tests.Rendering;

/// <summary>
/// As cinco janelas do quadro #540 a 720×480 nos dois temas. Cada uma confere, na ordem em que o jogador lê, todos os textos e botões do quadro;
/// os PNG seguem <c>VNFORGE_RENDER_DIR</c> (senão uma pasta temporária) e se conferem lado a lado com a captura do quadro.
/// </summary>
[Trait("Risco", "R8")]
public sealed class InstallerRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    public const string RenderVariable = "VNFORGE_RENDER_DIR";

    public static TheoryData<string> Themes => ["Dark"];

    private static string RenderDirectory =>
        Environment.GetEnvironmentVariable(RenderVariable) is { Length: > 0 } configured ? configured : Path.Combine(Path.GetTempPath(), "vnforge-installer-tests", "render");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Sep => Path.DirectorySeparatorChar.ToString();

    private static async Task<InstallerViewModel> FolderAsync(BoardEngine engine, FakeTimeProvider time)
    {
        var vm = new InstallerViewModel(engine, new NoShell(), new InstallerStart(null, Path.GetTempPath(), null), time);
        await vm.StartAsync();
        // Só o texto da pasta depende do disco da máquina de teste; o quadro mostra a pasta do jogo dele.
        vm.FolderPath = BoardEngine.Folder;
        return vm;
    }

    private static async Task<InstallerViewModel> StartedAtAsync(BoardEngine engine, FakeTimeProvider time)
    {
        var vm = new InstallerViewModel(engine, new NoShell(), new InstallerStart(BoardEngine.Folder, Path.GetTempPath(), null), time);
        await vm.StartAsync();
        return vm;
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_a_janela_2_instalando_com_63_por_cento(string theme)
    {
        var time = new FakeTimeProvider();
        var engine = new BoardEngine(time) { PauseAt = p => p is { Phase: PatchPhase.Apply, Done: 2, Path: not null } };
        var vm = await FolderAsync(engine, time);
        var install = vm.InstallCommand.ExecuteAsync(null);
        await Task.Run(() => engine.Reached.Wait(TimeSpan.FromMinutes(3)), Ct);
        try
        {
            await RenderAsync(vm, "540-inst-1", theme, (window, texts) =>
            {
                Assert.Equal(
                    [
                        "Tradução PT-BR · Amaoto no Hakoniwa",
                        "Amaoto no Hakoniwa",
                        "Tradução para português do Brasil Versão 1.2.0 · Equipe Chuva Mansa",
                        "Instalando",
                        "Não feche o instalador. Leva cerca de 20 segundos.",
                        "Conferir a versão do jogo",
                        "0,2 s",
                        "Guardar a cópia dos originais",
                        "4 arquivos · 218 MB",
                        "Aplicar a tradução",
                        "63%",
                        "Conferir o resultado",
                        "Cancelar",
                    ],
                    texts);
                Assert.Empty(EnabledButtons(window));
                var bar = window.GetVisualDescendants().OfType<ProgressBar>().Single();
                Assert.InRange(bar.Value, 62.5, 63.5);
                window.Close();
                Assert.True(window.IsVisible, "a janela não fecha com o instalador trabalhando");
            });
        }
        finally
        {
            engine.Release.Set();
            await install;
        }
    }

    private static byte[] SolidPng(int width, int height, byte red, byte green, byte blue)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        using (var buffer = bitmap.Lock())
        {
            var row = new byte[buffer.RowBytes];
            for (var x = 0; x < width; x++)
            {
                row[x * 4] = blue;
                row[(x * 4) + 1] = green;
                row[(x * 4) + 2] = red;
                row[(x * 4) + 3] = 255;
            }

            for (var y = 0; y < height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(row, 0, buffer.Address + (y * buffer.RowBytes), row.Length);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static (byte R, byte G, byte B) PixelAt(Window window, int x, int y)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var value = System.Runtime.InteropServices.Marshal.ReadInt32(buffer.Address + (y * buffer.RowBytes) + (x * 4));
        return buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888 ? ((byte)(value >> 16), (byte)(value >> 8), (byte)value) : ((byte)value, (byte)(value >> 8), (byte)(value >> 16));
    }

    private static string[] EnabledButtons(Window window) =>
    [
        .. VisibleButtons(window).Where(b => b.IsEffectivelyEnabled).Select(ButtonText),
    ];

    private static IEnumerable<Button> VisibleButtons(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Bounds.Height > 0 && !b.Classes.Contains("caption-button"))
            .OrderBy(b => b.TranslatePoint(default, window)!.Value.Y).ThenBy(b => b.TranslatePoint(default, window)!.Value.X);

    private static string ButtonText(Button button) =>
        button.GetVisualDescendants().OfType<TextBlock>().First().Text ?? string.Empty;

    /// <summary>
    /// Os textos visíveis, na ordem de leitura: a barra de título, a coluna da esquerda de cima para baixo e a coluna principal por linhas (da esquerda para a direita
    /// dentro da linha). O texto da capa é arte, não texto da janela, e fica de fora.
    /// </summary>
    private static List<string> ReadingOrder(Window window)
    {
        var items = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text) && t.Name is not ("CoverTitle" or "SealGlyph"))
            .Select(t => (Block: t, Point: t.TranslatePoint(default, window)!.Value))
            .ToList();
        var result = new List<string>();
        result.AddRange(items.Where(i => i.Point.Y < 32).OrderBy(i => i.Point.X).Select(i => Normalize(i.Block.Text!)));
        result.AddRange(items.Where(i => i.Point.Y >= 32 && i.Point.X < 200).OrderBy(i => i.Point.Y).Select(i => Normalize(i.Block.Text!)));
        var main = items.Where(i => i.Point.Y >= 32 && i.Point.X >= 200).OrderBy(i => i.Point.Y).ToList();
        var rows = new List<List<(TextBlock Block, Point Point)>>();
        foreach (var item in main)
        {
            if (rows.Count > 0 && item.Point.Y - rows[^1][0].Point.Y < 12)
            {
                rows[^1].Add(item);
            }
            else
            {
                rows.Add([item]);
            }
        }

        result.AddRange(rows.SelectMany(r => r.OrderBy(i => i.Point.X)).Select(i => Normalize(i.Block.Text!)));
        return result;
    }

    private static string Normalize(string text) => text.Replace("\n", " ", StringComparison.Ordinal);

    private async Task RenderAsync(InstallerViewModel vm, string name, string themeName, Action<Window, List<string>> inspect)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var path = Path.Combine(RenderDirectory, $"{name}-{(theme == ThemeVariant.Light ? "claro" : "escuro")}.png");
        await fixture.Session.Dispatch(
            () =>
            {
                var window = new InstallerWindow { DataContext = vm, RequestedThemeVariant = theme };
                try
                {
                    window.Show();
                    Settle(window);
                    Assert.Equal(new Size(720, 480), window.ClientSize);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using (var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("O Avalonia.Headless não devolveu o quadro; confira o UseSkia na sessão."))
                    {
                        frame.Save(path, PngBitmapEncoderOptions.Default);
                    }

                    inspect(window, ReadingOrder(window));
                }
                finally
                {
                    // Com o instalador trabalhando em segundo plano, a janela ligada ao modelo seguiria mudando depois do render; sem o modelo ela fecha.
                    window.DataContext = null;
                    window.Close();
                }

                return 0;
            },
            Ct);
        Assert.True(new FileInfo(path).Length > 10_000);
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class NoShell : IInstallerShell
    {
        // A pasta do quadro não existe nesta máquina: o executável é o de onde o jogador joga.
        public string? FindGame(string folder, PatchManifest manifest) => Path.Combine(folder, "Game.exe");

        public void OpenGame(string executablePath)
        {
        }

        public void OpenReadMe(string text)
        {
        }
    }
}
