#if DEBUG
using Avalonia.Controls;
using Avalonia.Styling;
using VNForge.App.Gallery;

namespace VNForge.App.Tests.Rendering;

/// <summary>Renderiza a galeria de componentes nos dois temas, para conferir contra o design system.</summary>
public class GalleryRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task Galeria_renderiza_em_cada_tema(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var path = RenderOutput.PathFor(ScreenRenderer.FileName("galeria", theme));
        await fixture.Session.Dispatch(
            () => ScreenRenderer.Render(new ScrollViewer { Content = new ComponentGallery() }, theme, path, 760, 3400),
            TestContext.Current.CancellationToken);

        Assert.True(new FileInfo(path).Length > 10_000);
    }
}
#endif
