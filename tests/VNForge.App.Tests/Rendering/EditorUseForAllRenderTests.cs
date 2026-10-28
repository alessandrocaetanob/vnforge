using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O "Usar em todos" do inspetor com o conteúdo rolado até o fim (#462): o botão fica inteiro na área visível, acima da barra fixa de "Aprovar e próxima".
/// Os PNGs (<c>editor-v5-usar-em-todos</c>) vão para <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class EditorUseForAllRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const double Tolerance = 0.5;
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string, int, int> Cases => new()
    {
        { "Dark", 1440, 1000 },
        { "Light", 1440, 640 },
        { "Dark", 1280, 560 },
        { "Light", 1280, 640 },
        { "Dark", 1366, 600 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task O_conteudo_que_rola_deixa_embaixo_a_altura_da_barra_fixa_e_o_Usar_em_todos_cabe_inteiro_rolado_ate_o_fim(string themeName, int width, int height)
    {
        using var temp = new TempFolder();
        var root = CreateRepeatedLineProject(temp.SubFolder("projeto"));
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var editor = new EditorViewModel();
                var vm = new MainWindowViewModel(start) { Editor = editor };
                var window = new MainWindow { Width = width, Height = height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await editor.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                    Assert.True(await editor.GoToLineAsync(FitWarningRenderTests.Script, "yst00013:000001"));
                    ScreenRenderer.Settle(window);

                    var inspetor = window.GetVisualDescendants().OfType<LineDetailView>().Single();
                    Assert.True(editor.Detail!.HasSameSource);
                    var botao = inspetor.GetVisualDescendants().OfType<Button>().Single(b => b.Command == editor.Detail.UseForAllCommand);
                    var rolagem = botao.GetVisualAncestors().OfType<ScrollViewer>().First();
                    var coluna = (StackPanel)rolagem.Content!;
                    var barra = inspetor.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ActionsBar");
                    var tabs = width < 1400;

                    Assert.True(Math.Abs(coluna.Margin.Bottom - barra.Bounds.Height) <= Tolerance, $"a margem de baixo é {coluna.Margin.Bottom:0.#} e a barra fixa tem {barra.Bounds.Height:0.#} de altura");

                    rolagem.ScrollToEnd();
                    ScreenRenderer.Settle(window);
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName($"editor-v5-usar-em-todos-{width}x{height}", theme)));

                    var area = new Rect(botao.TranslatePoint(default, window)!.Value, botao.Bounds.Size);
                    var topoDaBarra = barra.TranslatePoint(default, window)!.Value.Y;
                    var visivel = new Rect(rolagem.TranslatePoint(default, window)!.Value, rolagem.Viewport);
                    Assert.True(area.Bottom <= topoDaBarra + Tolerance, $"o botão termina em {area.Bottom:0.#} e a barra fixa começa em {topoDaBarra:0.#}");

                    if (tabs)
                    {
                        // Nas abas o botão é o último bloco visível da aba Tradução: com a margem ele fica a uma barra de altura do fim da área visível, acima do degradê.
                        Assert.True(rolagem.Offset.Y > 0, "a aba rola nesta altura de janela");
                        Assert.True(area.Top >= visivel.Top - Tolerance && area.Bottom <= visivel.Bottom - barra.Bounds.Height + Tolerance, $"o botão {area} está dentro da área visível {visivel}, acima do fim");
                    }
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static string CreateRepeatedLineProject(string parent)
    {
        var folder = ProjectFolder.Create(parent, new ProjectConfig(
            "Fala repetida",
            new GameConfig(@"D:\Jogos\MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        folder.SaveText(
            "yst00013",
            Enumerable.Range(1, 4).Select(i => new TextUnit($"yst00013:{i:000000}", TextUnitKind.Dialogue, "Misaki", "「おはよう」", "Bom dia.", TranslationStatus.Translated)).ToList());
        return folder.Root;
    }
}
