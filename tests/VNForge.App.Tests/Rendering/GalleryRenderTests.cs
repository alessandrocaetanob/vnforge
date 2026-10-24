#if DEBUG
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
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
            () =>
            {
                var gallery = new ComponentGallery();
                ScreenRenderer.Render(new ScrollViewer { Content = gallery }, theme, path, 760, 3400, window => Inspect(gallery));

            },
            TestContext.Current.CancellationToken);

        Assert.True(new FileInfo(path).Length > 10_000);
    }

    [Fact]
    public async Task Galeria_nos_dois_temas_na_mesma_janela_mantem_um_cartao_marcado_em_cada_coluna()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var window = new ComponentGalleryWindow();
                try
                {
                    window.Show();
                    var radios = window.GetVisualDescendants().OfType<RadioButton>().ToList();
                    Assert.Equal([true, false, true, false], radios.Select(r => r.IsChecked == true));
                }
                finally
                {
                    window.Close();
                }
            },
            TestContext.Current.CancellationToken);
    }

    private static void Inspect(ComponentGallery gallery)
    {
        Assert.Equal(5, gallery.GetVisualDescendants().OfType<PipelineStepItem>().Count());
        Assert.Equal(10, gallery.GetVisualDescendants().OfType<LegendItem>().Count());
        Assert.Equal(6, gallery.GetVisualDescendants().OfType<LineRow>().Count());
        Assert.Equal(4, gallery.GetVisualDescendants().OfType<ScriptItem>().Count());
        Assert.Equal(3, gallery.GetVisualDescendants().OfType<AiSuggestion>().Count());
        Assert.Equal(7, gallery.GetVisualDescendants().OfType<SeverityTag>().Count());
        Assert.All(gallery.GetVisualDescendants().OfType<Control>().Where(c => c is PipelineStepItem or LineRow or StatCard or FilterChip), c => Assert.True(c.Bounds.Height > 0));
    }
}
#endif
