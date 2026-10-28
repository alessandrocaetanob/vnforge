using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>O detalhe da linha do Editor com o aviso de "mesmo original, traduções diferentes" (#457): <c>editor-consistencia-escuro.png</c> e <c>editor-consistencia-claro.png</c> em <c>VNFORGE_RENDER_DIR</c>.</summary>
public sealed class EditorConsistencyRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static TextUnit Line(int n, string translation) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, null, "真弓は校長室に行く前に、職員室に立ち寄った。\\p", translation, TranslationStatus.Translated);

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_cartao_no_detalhe_da_linha(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Translated);
        ProjectFolder.Open(root).SaveText("yst00001",
        [
            Line(0, @"Antes de ir à sala do diretor, Mayumi passou na sala dos professores.\p"),
            Line(1, @"Antes de ir à sala do diretor, Mayumi passou na sala dos professores.\p"),
            Line(2, @"Antes de ir à sala do diretor, Mayumi passou na sala dos professores\p"),
            Line(3, @"Antes de ir à sala do diretor Mayumi passou  na sala dos professores.\p"),
        ]);

        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var editor = new EditorViewModel();
                var vm = new MainWindowViewModel(start) { Editor = editor };
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await editor.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                    await editor.GoToLineAsync("yst00001", "yst00001:000003");
                    await editor.RecheckAsync();
                    ScreenRenderer.Settle(window);

                    var card = Assert.Single(window.GetVisualDescendants().OfType<ConsistencyCardView>(), c => c.IsEffectivelyVisible);
                    Assert.True(card.Bounds.Height > 100);
                    card.BringIntoView();
                    ScreenRenderer.Settle(window);
                    var buttons = card.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
                    Assert.True(buttons.Count >= 4, "Usar em todas por variante e Manter diferentes");
                    Assert.All(buttons, b => Assert.DoesNotContain("accent", b.Classes));
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("editor-consistencia", theme)));
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }
}
