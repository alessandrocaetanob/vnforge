using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Bible;
using VNForge.AI.Cost;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Bible;
using VNForge.Core.Glossary;
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
        // As cores do quadro: Kenta verde, Misaki rosa, Ren laranja e Aoi azul.
        folder.SaveCharacters([new CharacterEntry("健太", "Kenta", Color: "green"), new CharacterEntry("美咲", "Misaki", Color: "rose"), new CharacterEntry("蓮", "Ren", Color: "orange"), new CharacterEntry("葵", "Aoi", Color: "blue")]);
        folder.SaveText("yst00001", SceneLines(("健太", 6), ("美咲", 9), ("蓮", 4), ("葵", 2)));
        folder.SaveBible(GameBible.From([new BibleScene("yst00001") { Summary = "Versão anterior." }]));
        folder.KeepPreviousBible();
        folder.SaveBible(GameBible.From(
        [
            new BibleScene("yst00001")
            {
                Name = "Atraso no portão",
                Summary = "Na chuva da manhã, Kenta e Ren chegam ao portão da escola e encontram o professor responsável de braços cruzados. Misaki repreende Aizawa e Sakura por cinco minutos de atraso. Ren reclama do guarda-chuva que emprestou.",
                Characters = ["Kenta", "Misaki", "Ren", "Aoi"],
                Tone = ["cômico leve", "tenso no fim", "seco na fala de Misaki"],
                Jokes = ["o guarda-chuva de Ren", "\"cinco minutos\""],
                Relations = [new BibleRelation("Kenta e Ren", "Amigos de infância, se provocam o tempo todo."), new BibleRelation("Misaki e Sakura", "Professora e aluna. Tom seco, sem ironia.")],
                Edited = [BibleField.Name],
            },
            new BibleScene("yst00002") { Name = "Volta para casa", Summary = "Ren volta sozinho.", Jokes = ["o guarda-chuva de Ren"] },
        ]) with
        {
            Synopsis = "Kenta se muda para uma cidade onde chove todo semestre e descobre que um guarda-chuva perdido liga três colegas de turma. A história segue os cinco meses até o festival da escola.",
            Timeline = [new BibleTimelineEntry("Prólogo", "A chuva e a mudança de cidade"), new BibleTimelineEntry("Parte 1", "Um semestre começa molhado"), new BibleTimelineEntry("Capítulo 2", "O festival e o caderno perdido")],
            Generation = new BibleGeneration(Now, "claude-opus-5-5", 2, 1, 1.8m),
        });
        AiUsageLog.TryRecord(folder, new AiUsageTotals(0, 0, 0, 0, 0.8m, 1));
        return root;
    }

    private static IReadOnlyList<TextUnit> SceneLines(params (string Speaker, int Count)[] speakers) =>
    [
        .. speakers.SelectMany(s => Enumerable.Range(0, s.Count).Select(i => (s.Speaker, i)))
            .Select((line, index) => new TextUnit($"yst00001:{index:D6}", TextUnitKind.Dialogue, line.Speaker, $"「相沢、佐倉。二人そろって五分遅刻だ」{line.i}", $"Aizawa, Sakura. Os dois, cinco minutos atrasados. {line.i}", TranslationStatus.Machine)),
    ];

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
                // Teto baixo para o pedido de R$ 0,43 aparecer na barra de custo depois do já gasto, como o trecho azul do quadro.
                vm.Bible.Limits = () => new BudgetLimits(14.5m, null);
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
                "Cenas", "Cena 1", "Atraso no portão", "Volta para casa", "Resumo", "Quem aparece", "Tom", "Piadas que voltam", "Relações",
                "Sinopse do jogo", "Linha do tempo", "Prólogo", "Custo da bíblia", "Gerada em", "Cenas lidas", "Reler um capítulo", "Exportar", "Voltar à última geração",
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
            Assert.Contains("professor responsável de braços cruzados", summary.Text, StringComparison.Ordinal);
            Assert.Equal("Resumo", AutomationProperties.GetName(summary));

            // O quadro mostra quem aparece, o tom, as piadas e as relações como chips e lista, sem caixa de texto, e não tem título nem frase de explicação.
            foreach (var expected in new[]
            {
                "fala 6 vezes", "fala 9", "cômico leve", "tenso no fim", "seco na fala de Misaki", "o guarda-chuva de Ren · cenas 1 e 2", "Amigos de infância, se provocam o tempo todo.",
                "Misaki e Sakura", "O jogo", "sinopse e linha do tempo", "Gerada em",
            })
            {
                Assert.Contains(expected, texts, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("O que a IA entendeu de cada cena", texts, StringComparison.Ordinal);
            Assert.All(
                view.GetVisualDescendants().OfType<TextBox>().Where(t => t.Classes.Contains("inline-box")),
                box => Assert.False(box.IsEffectivelyVisible, $"{AutomationProperties.GetName(box)} abre só ao clicar"));
            Assert.True(view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UndoButton").IsEffectivelyEnabled);

            // O estado do quadro: o resumo em edição, com a borda de destaque e sem o botão de reescrever na linha dele.
            summary.Focus();
            // O quadro foi capturado entre duas piscadas: sem o cursor, só a borda de destaque marca o campo.
            summary.CaretBrush = Avalonia.Media.Brushes.Transparent;
            ScreenRenderer.Settle(window);
            var summaryCard = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("summary"));
            Assert.Equal(1.5, summaryCard.BorderThickness.Left);
            Assert.DoesNotContain(summaryCard.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("rewrite") && b.IsEffectivelyVisible);
            Assert.DoesNotContain('—', texts);
            Assert.DoesNotContain('–', texts);

            var clipped = ClippedTextHeadlessTests.FindClipped(view);
            Assert.True(clipped.Count == 0, string.Join("; ", clipped));

            Snapshot(window, "biblia-v6", theme);
        });
    }

    [Fact]
    public async Task Clicar_na_leitura_do_campo_abre_a_caixa_de_texto_e_sair_dela_a_fecha()
    {
        await WithBiblePage("Dark", async (window, vm, _) =>
        {
            vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Bible);
            await vm.Bible.Loading;
            ScreenRenderer.Settle(window);
            var view = Assert.Single(window.GetVisualDescendants().OfType<BibleView>(), v => v.IsEffectivelyVisible);
            var field = view.GetVisualDescendants().OfType<Panel>().Single(p => p.Name == "ToneField");
            var reading = field.Children.OfType<Border>().Single();
            var box = field.Children.OfType<TextBox>().Single();
            Assert.True(reading.IsEffectivelyVisible);
            Assert.False(box.IsEffectivelyVisible);

            window.MouseDown(reading.TranslatePoint(new Point(8, 8), window)!.Value, MouseButton.Left);
            window.MouseUp(reading.TranslatePoint(new Point(8, 8), window)!.Value, MouseButton.Left);
            ScreenRenderer.Settle(window);
            Assert.True(box.IsEffectivelyVisible);
            Assert.False(reading.IsEffectivelyVisible);
            Assert.True(box.IsFocused);

            view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SummaryBox").Focus();
            ScreenRenderer.Settle(window);
            Assert.False(box.IsEffectivelyVisible);
            Assert.True(reading.IsEffectivelyVisible);
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
            var sideScroll = view.GetVisualDescendants().OfType<ScrollViewer>()
                .Where(s => s.IsEffectivelyVisible && s.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled && s.Extent.Width > s.Viewport.Width + 1)
                .Select(s => $"{s.TemplatedParent?.GetType().Name} {(s.TemplatedParent as Control)?.Name} extent {s.Extent.Width:0} viewport {s.Viewport.Width:0}").ToList();
            Assert.True(sideScroll.Count == 0, string.Join("; ", sideScroll));

            var sceneList = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "SceneList");
            var summary = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SummaryBox");
            Assert.True(sceneList.Bounds.Width >= 196, $"lista de cenas com {sceneList.Bounds.Width:0} px");
            Assert.True(summary.Bounds.Width >= 270, $"campo do resumo com {summary.Bounds.Width:0} px");

            var texts = VisibleText(view);
            var openInEditor = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OpenInEditorButton");
            Assert.True(openInEditor.IsEffectivelyVisible && openInEditor.Bounds.Right <= view.Bounds.Right, "o botão do Editor fica à vista, só com o ícone");
            Assert.Equal("Ir para a cena no Editor", AutomationProperties.GetName(openInEditor));
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
            Assert.Contains("O que a IA vai ler", texts, StringComparison.Ordinal);
            DialogProbe.AssertCentered(window, 640);
            Assert.Contains("Custo (aproximado): R$ ", texts, StringComparison.Ordinal);
            Assert.Contains("Já gasto no projeto: R$ ", texts, StringComparison.Ordinal);
            Assert.Contains("Campos que você editou (1 cena)", texts, StringComparison.Ordinal);
            Assert.Contains("preservados", texts, StringComparison.Ordinal);
            Assert.Contains("cache de prompt cobra 5% da entrada", texts, StringComparison.Ordinal);
            Assert.DoesNotContain('—', texts);
            var confirm = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConfirmGenerateButton");
            Assert.True(confirm.IsEffectivelyEnabled && confirm.Classes.Contains("accent"));

            Snapshot(window, "biblia-v6-gerar", theme);

            confirm.Command!.Execute(null);
            await vm.Bible.Generation;
            ScreenRenderer.Settle(window);
            Assert.False(vm.Bible.IsGenerateOpen);
            Assert.StartsWith("2 cenas escritas", vm.Bible.Notice, StringComparison.Ordinal);
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task O_inspetor_do_Editor_mostra_o_cartao_Resumo_da_cena_com_o_selo_Biblia(string themeName)
    {
        await WithBiblePage(themeName, async (window, vm, theme) =>
        {
            vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
            await vm.Editor.Loading;
            ScreenRenderer.Settle(window);
            vm.Editor.SelectedLine = vm.Editor.Lines.First(l => l.SpeakerName == "Misaki");
            ScreenRenderer.Settle(window);

            var card = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "SceneSummaryCard" && b.IsEffectivelyVisible);
            var texts = VisibleText(card);
            Assert.Contains("Resumo da cena", texts, StringComparison.Ordinal);
            Assert.Contains("Bíblia", texts, StringComparison.Ordinal);
            Assert.Contains("professor responsável de braços cruzados", texts, StringComparison.Ordinal);
            Assert.DoesNotContain("cômico leve", texts, StringComparison.Ordinal);

            // O quadro: a grade tem um retrato redondo ao lado de cada nome e nenhuma coluna Linha; "Na cena" vem recolhido, com a conta de falas.
            var grid = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "LinesList");
            Assert.Contains(grid.GetVisualDescendants().OfType<Portrait>(), p => p.IsEffectivelyVisible && p.Size == PortraitSize.Sm);
            Assert.DoesNotContain("Linha", VisibleText(window.GetVisualDescendants().OfType<EditorView>().Single(v => v.IsEffectivelyVisible)).Split('\n'));
            var scene = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SceneToggle");
            Assert.True(scene.IsEffectivelyVisible, "o Na cena é um cabeçalho que abre");
            Assert.False(window.GetVisualDescendants().OfType<ContextLines>().Single(c => c.Name == "SceneLines").IsEffectivelyVisible, "o Na cena começa recolhido");

            // Uma linha já traduzida não oferece "Sugerir" nem os chips de reescrever: o quadro não os mostra neste estado.
            var inspectorText = VisibleText(window.GetVisualDescendants().OfType<LineDetailView>().Single(v => v.IsEffectivelyVisible));
            Assert.DoesNotContain("Sugerir", inspectorText, StringComparison.Ordinal);
            Assert.DoesNotContain("Reescrever com um toque", inspectorText, StringComparison.Ordinal);

            Snapshot(window, "biblia-v6-editor", theme);
        });
    }
}
