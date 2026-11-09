using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Images;
using VNForge.Core.Projects;

namespace VNForge.App.Tests;

/// <summary>A tela de imagens de verdade (tema, estilos, recursos e Skia): mostra original e traduzida lado a lado e o diálogo de regravação.</summary>
public class ImagesViewHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const string Pack = "img.pak";
    private const string Gate = "bg/portao.png";

    private static Window Abrir(ImagesViewModel vm)
    {
        var window = new Window { Width = 1440, Height = 1000, Content = new ImagesView { DataContext = vm } };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    private static Button Botao(Window window, string name) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) == name);

    [Fact]
    public async Task ShowsTheOriginalAndThePreviewSideBySideAndTheRewriteDialog()
    {
        using var temp = new TempFolder();
        var game = temp.SubFolder("jogo");
        var composer = new AvaloniaImageComposer();
        byte[] png = [];
        await fixture.Session.Dispatch(
            () => png = composer.Render(new RasterPicture(320, 180, Enumerable.Repeat((byte)120, 320 * 180 * 4).ToArray()), new ImageEdit(Pack, Gate, "x", 320, 180, new ImageRegion(1, 1, 2, 2), ImageEraseModes.Solid, "#336699", string.Empty, "Noto Sans", 12, false, "#000000", null, false)),
            TestContext.Current.CancellationToken);
        ImagesViewModelTests.FakeArchive.Write(Path.Combine(game, Pack), new Dictionary<string, byte[]> { [Gate] = png });
        var folder = ProjectFolder.Create(temp.SubFolder("projeto"), new ProjectConfig(
            "Teste", new GameConfig(game, "jogo.exe"), new EngineConfig("yuris"), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig("fold"), AiConfig.Default));
        folder.SaveImageInventory([new ImageTextEntry(Pack, Gate, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(png)), 320, 180, "桜ヶ丘高校")]);
        var vm = new ImagesViewModel([new ImagesViewModelTests.ArchiveEngine()], composer, TimeSpan.Zero);
        vm.Project = ProjectSummaryReader.Read(folder.Root);
        await vm.Loading;

        await fixture.Session.Dispatch(
            async () =>
            {
                var window = Abrir(vm);
                vm.SelectedItem = vm.Items.Single();
                await vm.Selecting;
                vm.Text = "Portão";
                await vm.Previewing;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var images = window.GetVisualDescendants().OfType<Image>().Where(i => i.Source is Bitmap).ToList();
                Assert.Equal(2, images.Count);
                Assert.All(images, i => Assert.Equal((320, 180), (((Bitmap)i.Source!).PixelSize.Width, ((Bitmap)i.Source!).PixelSize.Height)));

                await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)Botao(window, "Marcar para o build").Command!).ExecuteAsync(null);
                await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)Botao(window, "Regravar as imagens no pacote").Command!).ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.True(vm.IsConfirming);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "portao.png");
                Assert.Equal(1, vm.MarkedCount);
                window.Close();
            },
            TestContext.Current.CancellationToken);
    }
}
