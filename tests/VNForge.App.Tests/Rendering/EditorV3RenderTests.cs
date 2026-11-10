using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
/// O Editor e o Modo revisão no design system v3 (#546), nos dois temas, a 1440×1000 e a 1366×768, com o projeto de demonstração: a fala 0495 selecionada, como o quadro <c>#issue-546</c>.
/// Os PNGs (<c>editor-v3-*</c>) vão para <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class EditorV3RenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, int, int> Cases => new()
    {
        { "Dark", 1440, 1000 },
        { "Light", 1440, 1000 },
        { "Dark", 1366, 768 },
        { "Light", 1366, 768 },
    };

    private static ThemeVariant Of(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private Task<int> Run(Func<Task> body) =>
        fixture.Session.Dispatch<int>(
            async () =>
            {
                await body();
                return 0;
            },
            TestContext.Current.CancellationToken);

    private static async Task<(MainWindow Window, MainWindowViewModel Vm)> OpenDemo(TempFolder config, string demoRoot, ThemeVariant theme, int width, int height)
    {
        var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demoRoot), new FixedClock(Now));
        var editor = new EditorViewModel(scriptLabel: DemoProject.Label, clock: new FixedClock(Now));
        var vm = new MainWindowViewModel(start) { Editor = editor };
        var window = new MainWindow { Width = width, Height = height, RequestedThemeVariant = theme, DataContext = vm };
        window.Show();
        await start.OpenFolderCommand.ExecuteAsync(null);
        await vm.ProjectOpening;
        await editor.Loading;
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        Assert.True(await editor.GoToLineAsync(DemoProject.CurrentScript, "yst00013:0495"));
        ScreenRenderer.Settle(window);
        return (window, vm);
    }

    private static string Suffix(string themeName, int width) => $"{width}-{(themeName == "Light" ? "claro" : "escuro")}";

    private static Rect Frame(Visual root, Control c) => new Rect(c.Bounds.Size).TransformToAABB(c.TransformToVisual(root)!.Value);

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task O_Editor_no_v3_mede_como_o_quadro_com_a_fala_0495_selecionada(string themeName, int width, int height)
    {
        var demo = demoFolder;
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var (window, vm) = await OpenDemo(config, demo.Root, Of(themeName), width, height);
                try
                {
                    // Sem a linha de título, e sem os botões que viram passos do cabeçalho do jogo.
                    Assert.DoesNotContain(window.GetVisualDescendants().OfType<PageHeader>(), h => h.IsEffectivelyVisible);
                    Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && (b.Command == vm.Editor.CheckFileCommand || b.Command == vm.Editor.StartReviewCommand));

                    // Grade: cabeçalho de 38 px, linhas de 48, colunas 72 · 148 · 1* · 1* · 28 com vão de 10 e recuo de 16.
                    var lista = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "LinesList");
                    var grade = lista.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("panel"));
                    var cabecalho = grade.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("divider-bottom") && b.Child is Grid { ColumnDefinitions.Count: 5 });
                    Assert.Equal(38, cabecalho.Bounds.Height);
                    var colunas = ((Grid)cabecalho.Child!).ColumnDefinitions;
                    Assert.Equal([72d, 148d, 28d], new[] { colunas[0], colunas[1], colunas[4] }.Select(c => c.Width.Value));
                    Assert.InRange(colunas[2].ActualWidth - colunas[3].ActualWidth, -1, 1);
                    Assert.Equal(new Thickness(16, 0), cabecalho.Padding);
                    var titulos = ((Grid)cabecalho.Child).Children.OfType<TextBlock>().ToList();
                    Assert.All(titulos, t => Assert.Equal(FontWeight.SemiBold, t.FontWeight));
                    Assert.Equal(["Situação", "Quem fala", "Original", "Tradução"], titulos.Select(t => t.Text));

                    var linhas = lista.GetVisualDescendants().OfType<LineRow>().Where(l => l.IsEffectivelyVisible).ToList();
                    Assert.All(linhas, l => Assert.Equal(48, l.Bounds.Height));
                    var atual = Assert.Single(linhas, l => l.Number == 495);
                    var retrato = Assert.Single(atual.GetVisualDescendants().OfType<Portrait>());
                    Assert.Equal(28, retrato.Bounds.Width);
                    Assert.True(Math.Abs(Frame(window, retrato).X - Frame(window, titulos[1]).X) <= 0.5, "o retrato começa na coluna Quem fala");

                    // Cenas em 220 e inspetor em 384 a 1440 (o inspetor cede abaixo de 1400), recuados 24 px da borda.
                    var cenas = window.GetVisualDescendants().OfType<ScenesPanel>().First();
                    var painelCenas = cenas.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("panel"));
                    var inspetor = window.GetVisualDescendants().OfType<LineDetailView>().First(v => v.IsEffectivelyVisible);
                    if (width >= 1440)
                    {
                        Assert.Equal(220, painelCenas.Bounds.Width, 0.5);
                        Assert.Equal(384, inspetor.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("panel")).Bounds.Width, 0.5);
                        Assert.Equal(112, Frame(window, painelCenas).X, 0.5);
                    }

                    // Lista de cenas: linhas de 32 px, a atual com a barra de 3 px.
                    if (width >= 1440)
                    {
                        var item = Assert.Single(window.GetVisualDescendants().OfType<ScriptItem>(), i => i.IsEffectivelyVisible && i.Key == DemoProject.CurrentScript);
                        Assert.Equal(32, item.Bounds.Height);
                        Assert.True(item.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_SelectionBar").IsVisible);
                    }

                    // Pé da grade: resumo, barra de 8 px e legenda, nessa ordem.
                    var resumo = grade.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == vm.Editor.LinesSummary);
                    var barra = grade.GetVisualDescendants().OfType<DistributionBar>().Single();
                    var legenda = grade.GetVisualDescendants().OfType<Legend>().Single();
                    Assert.Equal(8, barra.Bounds.Height);
                    Assert.True(Frame(window, resumo).Bottom <= Frame(window, barra).Top && Frame(window, barra).Bottom <= Frame(window, legenda).Top, "resumo, barra e legenda em coluna");

                    ScreenRenderer.Save(window, RenderOutput.PathFor($"editor-v6-{Suffix(themeName, width)}.png"));
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task O_Modo_revisao_no_v3_tem_os_recuos_o_palco_e_a_caixa_do_quadro(string themeName, int width, int height)
    {
        var demo = demoFolder;
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var (window, vm) = await OpenDemo(config, demo.Root, Of(themeName), width, height);
                try
                {
                    vm.Editor.StartReviewCommand.Execute(null);
                    ScreenRenderer.Settle(window);
                    Assert.True(vm.Editor.IsReviewing);
                    ScreenRenderer.Save(window, RenderOutput.PathFor($"modo-revisao-v6-{Suffix(themeName, width)}.png"));
                    var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
                    var stage = Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>());
                    var script = view.FindControl<ScrollViewer>("Script")!;
                    var voltar = view.GetVisualDescendants().OfType<Button>().First(b => b.Command == vm.Editor.Review!.CloseCommand);
                    var barra = view.FindControl<Border>("ActionBar")!;

                    // Recuo de 32 dos lados, 24 em cima e 16 embaixo, e 32 entre o roteiro e o palco.
                    var origem = Frame(window, view);
                    var editor = view.GetVisualAncestors().OfType<EditorView>().First();
                    Assert.Equal(24, Frame(window, view.FindControl<Grid>("TopBar")!).Y - Frame(window, editor).Y, 0.5);
                    Assert.True(Frame(window, voltar).Height == 28);
                    Assert.Equal(80 + 32, Frame(window, script).X, 0.5);
                    var vao = Frame(window, stage).X - Frame(window, script).Right;
                    Assert.InRange(vao, 32, 32 + 12 + 0.5);

                    var box = Assert.Single(stage.GetVisualDescendants().OfType<GameTextbox>());
                    var caixa = Frame(window, box);
                    var palco = Frame(window, stage);
                    Assert.True(Math.Abs(((caixa.Top + caixa.Bottom) / 2) - ((palco.Top + palco.Bottom) / 2)) <= 12, "a caixa está no centro do palco");
                    Assert.True(Frame(window, barra).Bottom <= window.Bounds.Height - 28);
                    Assert.Empty(ClippedTextHeadlessTests.FindClipped(view));
                }
                finally
                {
                    window.Close();
                }
            });
    }
}
