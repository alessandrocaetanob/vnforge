using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Images;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O editor de imagens (#535) a 1440×1000 nos dois temas, no design system v3: a faixa de miniaturas, original e traduzida lado a lado, o painel de
/// propriedades e o diálogo que gera o build. Os PNG seguem <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class ImagesRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const string Pack = "img.pak";
    private const string Gate = "bg/portao.png";
    private const int Width = 1280;
    private const int Height = 720;

    public static TheoryData<string> Themes => ["Dark", "Light"];

    // O desenho de exemplo do quadro: céu em degradê, placa clara com o nome da escola em japonês, chão escuro.
    private static byte[] SchoolGate(AvaloniaImageComposer composer, bool withText)
    {
        var bgra = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var at = ((y * Width) + x) * 4;
                var sign = x is >= 400 and < 880 && y is >= 200 and < 380;
                (bgra[at], bgra[at + 1], bgra[at + 2], bgra[at + 3]) = sign
                    ? ((byte)216, (byte)228, (byte)233, (byte)255)
                    : y > 500 ? ((byte)48, (byte)28, (byte)22, (byte)255) : ((byte)(143 - (y / 8)), (byte)(95 - (y / 12)), (byte)(74 - (y / 14)), (byte)255);
            }
        }

        var edit = new ImageEdit(Pack, Gate, "x", Width, Height, new ImageRegion(420, 230, 440, 120), ImageEraseModes.Solid, "#d8e4e9", withText ? "桜ヶ丘高校" : string.Empty, "Noto Sans JP", 64, true, "#1d1d2a", null, false);
        return composer.Render(new RasterPicture(Width, Height, bgra), edit);
    }

    private static async Task<(ImagesViewModel Vm, TempFolder Temp)> Open(AvaloniaImageComposer composer)
    {
        var temp = new TempFolder();
        var game = temp.SubFolder("jogo");
        var gate = SchoolGate(composer, withText: true);
        var other = composer.Render(new RasterPicture(640, 360, Enumerable.Repeat((byte)90, 640 * 360 * 4).ToArray()), new ImageEdit(Pack, "ui/x.png", "x", 640, 360, new ImageRegion(0, 0, 1, 1), ImageEraseModes.Solid, "#335577", string.Empty, "Noto Sans", 12, false, "#000000", null, false));
        var entries = new Dictionary<string, byte[]> { [Gate] = gate, ["ui/titulo.png"] = other, ["ui/logo.png"] = other, ["bg/rua.png"] = other };
        ImagesViewModelTests.FakeArchive.Write(Path.Combine(game, Pack), entries);
        var folder = ProjectFolder.Create(temp.SubFolder("projeto"), new ProjectConfig(
            "Amaoto no Hakoniwa", new GameConfig(game, "jogo.exe"), new EngineConfig("yuris"), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig("fold"), AiConfig.Default));
        folder.SaveImageInventory(
        [
            new ImageTextEntry(Pack, Gate, Convert.ToHexStringLower(SHA256.HashData(gate)), Width, Height, "桜ヶ丘高校 校門", "Colégio Sakuragaoka, portão"),
            new ImageTextEntry(Pack, "ui/titulo.png", Convert.ToHexStringLower(SHA256.HashData(other)), 640, 360, "タイトル"),
            new ImageTextEntry(Pack, "ui/logo.png", Convert.ToHexStringLower(SHA256.HashData(other)), 640, 360, "ロゴ"),
            new ImageTextEntry(Pack, "bg/rua.png", Convert.ToHexStringLower(SHA256.HashData(other)), 640, 360, string.Empty),
        ]);
        var vm = new ImagesViewModel([new ImagesViewModelTests.ArchiveEngine()], composer, TimeSpan.Zero);
        vm.Project = ProjectSummaryReader.Read(folder.Root);
        await vm.Loading;
        foreach (var item in vm.Items)
        {
            _ = item.ThumbnailPng;
            await item.ThumbnailLoading;
        }

        vm.SelectedItem = vm.Items.First(i => i.Path == Gate);
        await vm.Selecting;
        vm.SetRegion(420, 230, 440, 120);
        vm.Text = "Colégio Sakuragaoka";
        vm.Font = "Zen Kaku Gothic New";
        vm.FontSize = 40;
        await vm.Previewing;
        return (vm, temp);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task RenderizaOEditorComAFaixaDeMiniaturasEAPreviaLadoALado(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                var (vm, temp) = await Open(new AvaloniaImageComposer());
                using (temp)
                {
                    await ((IAsyncRelayCommand)vm.MarkCommand).ExecuteAsync(null);
                    ScreenRenderer.Render(
                        new ImagesView { DataContext = vm },
                        theme,
                        RenderOutput.PathFor(ScreenRenderer.FileName("imagens", theme)),
                        inspect: window =>
                        {
                            var bitmaps = window.GetVisualDescendants().OfType<Image>().Where(i => i.Source is Bitmap).ToList();
                            Assert.True(bitmaps.Count >= 6, $"Esperava 4 miniaturas e as duas prévias, achei {bitmaps.Count} imagens.");
                            var strip = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("thumbs"));
                            var previews = bitmaps.Where(i => i.Bounds.Width > 300).ToList();
                            Assert.Equal(2, previews.Count);
                            Assert.True(previews.All(p => p.Bounds.Width > 400), "As duas prévias dividem a largura da página.");
                            var left = previews.Min(p => p.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value.X);
                            var right = previews.Max(p => p.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value.X);
                            Assert.True(right - left > 400, "Original e traduzida ficam lado a lado, não uma sobre a outra.");
                            Assert.True(strip.Bounds.Height >= 68);
                            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.StartsWith("Cabe na placa", StringComparison.Ordinal) == true);
                            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "O que muda no build");
                        });
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task RenderizaODialogoQueGeraOBuild(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                var (vm, temp) = await Open(new AvaloniaImageComposer());
                using (temp)
                {
                    await ((IAsyncRelayCommand)vm.MarkCommand).ExecuteAsync(null);
                    await ((IAsyncRelayCommand)vm.RewriteCommand).ExecuteAsync(null);
                    ScreenRenderer.Render(
                        new ImagesView { DataContext = vm },
                        theme,
                        RenderOutput.PathFor(ScreenRenderer.FileName("imagens-build", theme)),
                        inspect: window =>
                        {
                            Assert.True(vm.IsConfirming);
                            var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
                            Assert.Contains("portao.png", texts);
                            Assert.Contains(texts, t => t == "Cópia de segurança antes de gravar");
                            Assert.Contains(texts, t => t == "Regravar 1 imagem no PAK?");
                        });
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }
}
