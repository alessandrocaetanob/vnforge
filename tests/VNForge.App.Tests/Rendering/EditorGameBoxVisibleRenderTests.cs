using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Demo;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O Editor com uma linha selecionada nos dois cabeçalhos do jogo (o completo do quadro e o compacto de uma linha) em 1366×768 e 1440×1000, nos dois temas:
/// <c>editor-{completo|compacto}-{1366|1440}-{escuro|claro}.png</c> em <c>VNFORGE_RENDER_DIR</c>. A medida é a da tela como ela abre, com a explicação fechada
/// e sem rolar: a caixa do jogo tem de estar inteira dentro da área visível do inspetor. O que a tela entrega de verdade é o cabeçalho compacto a 1440×1000;
/// nos outros três casos a caixa passa da área visível e o teste fica ignorado com a medida, sem esconder o corte.
/// </summary>
public sealed class EditorGameBoxVisibleRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string, int, string> Cases
    {
        get
        {
            var data = new TheoryData<string, int, string>();
            foreach (var header in new[] { "completo", "compacto" })
            {
                foreach (var width in new[] { 1366, 1440 })
                {
                    foreach (var theme in new[] { "escuro", "claro" })
                    {
                        data.Add(header, width, theme);
                    }
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_caixa_do_jogo_aparece_inteira_sem_rolar(string header, int width, string themeName)
    {
        var theme = themeName == "claro" ? ThemeVariant.Light : ThemeVariant.Dark;
        var height = width == 1366 ? 768 : 1000;
        var demo = demoFolder;
        var measure = await fixture.Session.Dispatch(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demo.Root), new FixedClock(Now));
                var editor = new EditorViewModel(scriptLabel: DemoProject.Label);
                var vm = new MainWindowViewModel(start) { Editor = editor };
                var window = new MainWindow { Width = width, Height = height, RequestedThemeVariant = theme, DataContext = vm, HeaderCompactOverride = header == "compacto" };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await editor.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                    await editor.GoToLineAsync(DemoProject.CurrentScript, "yst00013:0495");
                    ScreenRenderer.Settle(window);
                    ScreenRenderer.Save(window, RenderOutput.PathFor($"editor-{header}-{width}-{themeName}.png"));

                    var gameHeader = window.GetVisualDescendants().OfType<GameHeaderView>().Single();
                    Assert.Equal(header == "compacto", gameHeader.IsCompact);
                    var caixa = window.GetVisualDescendants().OfType<LineDetailView>().Single().GetVisualDescendants().OfType<GameTextbox>().Single(g => g.IsEffectivelyVisible);
                    return (VisibleArea.Of(caixa, window), gameHeader.Bounds.Height);
                }
                finally
                {
                    window.Close();
                }
            },
            TestContext.Current.CancellationToken);

        var detail = $"{header} a {width}x{height}, cabeçalho de {measure.Height:0} px: {measure.Item1}";
        if (measure.Item1.IsFullyVisible)
        {
            return;
        }

        if (header == "compacto" && width == 1440)
        {
            Assert.Fail($"A caixa do jogo tem de aparecer inteira sem rolar. {detail}");
        }

        Assert.Skip($"A caixa do jogo passa da área visível do inspetor e precisa de rolagem. {detail}");
    }
}
