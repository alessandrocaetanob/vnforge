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

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task Galeria_v3_mostra_cada_controle_novo_em_cada_tema(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var path = RenderOutput.PathFor(ScreenRenderer.FileName("galeria-v3", theme));
        await fixture.Session.Dispatch(
            () =>
            {
                var gallery = new ComponentGalleryV3 { Margin = new Avalonia.Thickness(24) };
                ScreenRenderer.Render(new ScrollViewer { Content = gallery }, theme, path, 800, 5200, _ => InspectV3(gallery));
            },
            TestContext.Current.CancellationToken);

        Assert.True(new FileInfo(path).Length > 10_000);
    }

    private static void InspectV3(ComponentGalleryV3 gallery)
    {
        T[] All<T>() where T : Control => [.. gallery.GetVisualDescendants().OfType<T>()];

        Assert.Equal(ComponentGalleryV3.IconKeys.Count, All<PathIcon>().Count(i => i.Parent is StackPanel { Width: 84 }));
        Assert.Equal(8, All<MarkerChip>().Length);
        Assert.Equal(5, All<GameTextbox>().Length);
        Assert.Equal(4, All<ProgressRing>().Length);
        Assert.Equal(6, All<Seg3Bar>().Length);
        Assert.Equal(3, All<ChapterRow>().Length);
        Assert.Equal(2, All<CostLabel>().Length);
        Assert.Equal(2, All<BudgetBar>().Length);
        Assert.Equal(3, All<QualityCard>().Length);
        Assert.Single(All<ContextLines>());
        Assert.Single(All<ExplainCard>());
        Assert.Equal(2, All<ScriptGroup>().Length);
        Assert.Equal(3, All<GameStateBanner>().Length);
        Assert.Equal(3, All<TodoItem>().Length);
        Assert.Equal(2, All<TechnicalDetails>().Length);
        Assert.Single(All<ScreenDialog>());
        Assert.All(
            gallery.GetVisualDescendants().OfType<Control>().Where(c => c is GameTextbox or ProgressRing or Seg3Bar or ChapterRow or QualityCard or GameStateBanner or TodoItem),
            c => Assert.True(c.Bounds.Width > 0 && c.Bounds.Height > 0, $"{c.GetType().Name} sem tamanho"));
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
                    Assert.Equal([true, false, true, false], radios.Where(r => r is not QualityCard).Select(r => r.IsChecked == true));
                    Assert.Equal([false, true, false, false, true, false], radios.OfType<QualityCard>().Select(r => r.IsChecked == true));
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
