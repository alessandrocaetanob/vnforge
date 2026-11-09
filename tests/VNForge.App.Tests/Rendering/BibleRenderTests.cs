using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Bible;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Bible;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// A página Bíblia (#530) montada de verdade nos dois temas a 1440×1000: a lista de cenas com o nome da bíblia, os campos de uma cena com o selo de quem os
/// escreveu, a sinopse e o custo à direita, a janela "Gerar a bíblia do jogo" com o preço antes de rodar e o cartão "Resumo da cena" no inspetor do Editor.
/// Os PNG seguem <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class BibleRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static string CreateProject(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated, TranslationStatus.Untranslated);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00002", [new TextUnit("yst00002:000000", TextUnitKind.Dialogue, "美咲", "ただいま", null, TranslationStatus.Untranslated)]);
        folder.SaveBible(GameBible.From(
        [
            new BibleScene("yst00001")
            {
                Name = "Atraso no portão",
                Summary = "Na chuva da manhã, Kenta e Ren chegam ao portão e encontram o professor de braços cruzados.",
                Characters = ["Kenta", "Ren"],
                Tone = ["cômico leve", "tenso no fim"],
                Jokes = ["o guarda-chuva de Ren"],
                Relations = [new BibleRelation("Kenta e Ren", "Amigos de infância")],
                Edited = [BibleField.Name],
            },
            new BibleScene("yst00002") { Name = "Volta para casa", Summary = "Ren volta sozinho." },
        ]) with
        {
            Synopsis = "Kenta se muda para uma cidade onde chove todo semestre.",
            Timeline = [new BibleTimelineEntry("Prólogo", "A chuva e a mudança de cidade"), new BibleTimelineEntry("Capítulo 1", "Um semestre começa molhado")],
            Generation = new BibleGeneration(Now, "claude-opus-5-5", 2, 1, 1.8m),
        });
        return root;
    }

    private static string VisibleText(Control root) =>
        string.Join('\n', root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));

    private async Task WithBiblePage(string themeName, Func<Window, MainWindowViewModel, ThemeVariant, Task> body, int width = ScreenRenderer.Width)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                using var project = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(CreateProject(project)), new FixedClock(Now));
                var vm = new MainWindowViewModel(start) { BibleProviderFactory = (_, _) => new FakeBibleProvider() };
                var window = new MainWindow { Width = width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await body(window, vm, theme);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void Snapshot(Window window, string name, ThemeVariant theme)
    {
        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
        ScreenRenderer.Save(window, path);
        using var png = new Bitmap(path);
        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_pagina_mostra_as_cenas_os_campos_com_o_selo_a_sinopse_e_o_custo(string themeName)
    {
        await WithBiblePage(themeName, async (window, vm, theme) =>
        {
            Assert.Contains(vm.Nav, n => n.Key == NavKey.Bible && n.Label == "Bíblia" && n.IsEnabled && n.IsVisible);
            vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Bible);
            await vm.Bible.Loading;
            ScreenRenderer.Settle(window);

            var view = Assert.Single(window.GetVisualDescendants().OfType<BibleView>(), v => v.IsEffectivelyVisible);
            Assert.True(view.Bounds.Width > 1000 && view.Bounds.Right <= ScreenRenderer.Width, "a página cabe na janela");
            var texts = VisibleText(view);
            foreach (var expected in new[]
            {
                "Bíblia", "CENAS", "Cena 1", "Atraso no portão", "Volta para casa", "RESUMO", "QUEM APARECE", "TOM", "PIADAS QUE VOLTAM", "RELAÇÕES",
                "SINOPSE DO JOGO", "LINHA DO TEMPO", "Prólogo", "Custo da bíblia", "Gerada em", "Cenas lidas", "Reler um capítulo", "Exportar", "Voltar à última geração",
            })
            {
                Assert.Contains(expected, texts, StringComparison.Ordinal);
            }

            Assert.Contains("R$ ", texts, StringComparison.Ordinal);
            Assert.Contains("Todas as cenas", texts, StringComparison.Ordinal);
            Assert.StartsWith("Gerar de novo · R$ ", vm.Bible.GenerateLabel, StringComparison.Ordinal);

            // O nome da cena 1 é seu (Sua) e o resto é da IA: os dois selos aparecem na tela.
            var owners = view.GetVisualDescendants().OfType<Border>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("owner")).ToList();
            Assert.Contains(owners, b => b.Classes.Contains("mine"));
            Assert.Contains(owners, b => !b.Classes.Contains("mine"));

            var summary = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SummaryBox");
            Assert.Contains("professor de braços cruzados", summary.Text, StringComparison.Ordinal);
            Assert.Equal("Resumo", AutomationProperties.GetName(summary));
            Assert.DoesNotContain('—', texts);
            Assert.DoesNotContain('–', texts);

            var clipped = ClippedTextHeadlessTests.FindClipped(view);
            Assert.True(clipped.Count == 0, string.Join("; ", clipped));

            Snapshot(window, "biblia-v6", theme);
        });
    }

    [Theory]
    [InlineData(960)]
    [InlineData(1040)]
    [InlineData(1140)]
    public async Task Em_janela_estreita_as_colunas_encolhem_sem_rolar_de_lado_nem_cortar_texto(int width)
    {
        await WithBiblePage("Dark", async (window, vm, _) =>
        {
            vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Bible);
            await vm.Bible.Loading;
            vm.Bible.SelectedRow = vm.Bible.Rows.First();
            ScreenRenderer.Settle(window);

            var view = Assert.Single(window.GetVisualDescendants().OfType<BibleView>(), v => v.IsEffectivelyVisible);
            Assert.True(view.Bounds.Right <= width, $"a página ({view.Bounds.Right:0} px) cabe na janela de {width} px");
            Assert.DoesNotContain(
                view.GetVisualDescendants().OfType<ScrollViewer>(),
                s => s.IsEffectivelyVisible && s.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled && s.Extent.Width > s.Viewport.Width + 1);

            var sceneList = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "SceneList");
            var summary = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SummaryBox");
            Assert.True(sceneList.Bounds.Width >= 196, $"lista de cenas com {sceneList.Bounds.Width:0} px");
            Assert.True(summary.Bounds.Width >= 280, $"campo do resumo com {summary.Bounds.Width:0} px");

            var texts = VisibleText(view);
            Assert.Contains("Ir para a cena no Editor", texts, StringComparison.Ordinal);
            Assert.Contains("Filtrar cenas", view.GetVisualDescendants().OfType<TextBox>().Select(t => t.PlaceholderText), StringComparer.Ordinal);

            var clipped = ClippedTextHeadlessTests.FindClipped(view);
            Assert.True(clipped.Count == 0, string.Join("; ", clipped));
        }, width);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Gerar_a_biblia_mostra_o_preco_antes_de_rodar_e_so_chama_a_IA_ao_confirmar(string themeName)
    {
        await WithBiblePage(themeName, async (window, vm, theme) =>
        {
            vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Bible);
            ScreenRenderer.Settle(window);
            var generate = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "GenerateButton");

            generate.Command!.Execute(null);
            await vm.Bible.Generation;
            ScreenRenderer.Settle(window);

            var layer = OverlayLayer.GetOverlayLayer(window)!;
            var dialog = layer.Children.OfType<Control>().Single(c => c.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "ConfirmGenerateButton"));
            var texts = VisibleText(dialog);
            Assert.Contains("Gerar a bíblia do jogo", texts, StringComparison.Ordinal);
            Assert.Contains("O QUE A IA VAI LER", texts, StringComparison.Ordinal);
            Assert.Contains("Custo (aproximado): R$ ", texts, StringComparison.Ordinal);
            Assert.Contains("Já gasto no projeto: R$ ", texts, StringComparison.Ordinal);
            Assert.Contains("Campos que você editou (1 cena)", texts, StringComparison.Ordinal);
            Assert.Contains("preservados", texts, StringComparison.Ordinal);
            Assert.Contains("cache de prompt cobra 5% da entrada", texts, StringComparison.Ordinal);
            Assert.DoesNotContain('—', texts);
            var confirm = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConfirmGenerateButton");
            Assert.True(confirm.IsEffectivelyEnabled && confirm.Classes.Contains("accent"));

            DialogProbe.AssertCentered(window, 640);
            Snapshot(window, "biblia-v6-gerar", theme);

            confirm.Command!.Execute(null);
            await vm.Bible.Generation;
            ScreenRenderer.Settle(window);
            Assert.False(vm.Bible.IsGenerateOpen);
            Assert.StartsWith("2 cenas escritas", vm.Bible.Notice, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task O_inspetor_do_Editor_mostra_o_cartao_Resumo_da_cena_com_o_selo_Biblia()
    {
        await WithBiblePage("Dark", async (window, vm, theme) =>
        {
            vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
            await vm.Editor.Loading;
            ScreenRenderer.Settle(window);
            vm.Editor.SelectedLine = vm.Editor.Lines.First();
            ScreenRenderer.Settle(window);

            var card = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "SceneSummaryCard" && b.IsEffectivelyVisible);
            var texts = VisibleText(card);
            Assert.Contains("Resumo da cena", texts, StringComparison.Ordinal);
            Assert.Contains("Bíblia", texts, StringComparison.Ordinal);
            Assert.Contains("professor de braços cruzados", texts, StringComparison.Ordinal);
            Assert.Contains("cômico leve · tenso no fim", texts, StringComparison.Ordinal);

            Snapshot(window, "biblia-v6-editor", theme);
        });
    }
}
