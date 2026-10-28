using VNForge.Core.Demo;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// A Conferência com o QA de verdade sobre um projeto pequeno escrito para casar com as linhas do artboard "Conferir": marcadores perdidos, caixa
/// de quatro linhas, espaço duplo, espaço antes de pontuação, glossário e a mesma fala traduzida de três jeitos. Gera os PNG que vão ao lado do
/// artboard no PR: a primeira linha selecionada (erro) e a linha com correção proposta (Aplicar correção).
/// </summary>
public sealed class ConferenceRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_Conferir_a_traducao_com_erro_selecionado_e_com_correcao_de_um_clique(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = ArtboardProject.Create(temp.SubFolder("projeto"));

        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
                    await vm.ConferenceLoading;

                    // O primeiro problema da lista: erro, sem correção automática.
                    Assert.Equal("marcador-diferente", vm.Conference.SelectedRow?.RuleId);
                    ScreenRenderer.Settle(window);
                    var view = window.GetVisualDescendants().OfType<ConferenceView>().Single();
                    Assert.True(vm.Conference.HasFixable);
                    AssertHeader(view, vm.Conference);
                    AssertBlockingBanner(window, view, vm.Conference, theme);
                    AssertSceneNamesAndChips(view);

                    // O cartão de "mesmo original": as duas ações não são o botão de destaque (esse é um por tela, no cabeçalho).
                    var cardButtons = view.GetVisualDescendants().OfType<Button>()
                        .Where(b => b.IsEffectivelyVisible && (b.Command == vm.Conference.UseVariantCommand || b.Command == vm.Conference.KeepDifferentCommand)).ToList();
                    Assert.True(cardButtons.Count >= 4, "o cartão mostra Usar em todas por variante e Manter diferentes");
                    Assert.All(cardButtons, b => Assert.DoesNotContain("accent", b.Classes));
                    RawMarkers.AssertCardUsesChips(view.GetVisualDescendants().OfType<ConsistencyCardView>().Single(c => c.IsEffectivelyVisible), minChips: 4);

                    // Erro sem correção automática: só "Abrir no editor", em destaque, na barra do pé do painel.
                    var open = ActionButton(view, vm.Conference.OpenLineCommand);
                    Assert.Contains("accent", open.Classes);
                    Assert.DoesNotContain(PanelButtons(view), b => b.Command == vm.Conference.FixAndNextCommand && b.IsEffectivelyVisible);
                    AssertInsidePanelFooter(window, view, open);
                    Save(window, "conferir-v3-problemas", theme);

                    // O espaço duplo da mesma linha traz a correção proposta e o botão "Aplicar correção".
                    vm.Conference.SelectedRow = vm.Conference.Rows.First(r => r.RuleId == "espaco-duplo");
                    Assert.True(vm.Conference.SelectedRow.HasProposal);
                    ScreenRenderer.Settle(window);

                    // Com correção: "Corrigir e ir para o próximo" em destaque, as outras duas ao lado, todas no pé do painel.
                    var fixAndNext = ActionButton(view, vm.Conference.FixAndNextCommand);
                    var markOk = ActionButton(view, vm.Conference.MarkOkCommand);
                    var openEditor = ActionButton(view, vm.Conference.OpenLineCommand);
                    Assert.Contains("accent", fixAndNext.Classes);
                    Assert.DoesNotContain("accent", openEditor.Classes);
                    AssertInsidePanelFooter(window, view, fixAndNext);
                    AssertInsidePanelFooter(window, view, markOk);
                    AssertInsidePanelFooter(window, view, openEditor);
                    Assert.True(fixAndNext.Bounds.Width > openEditor.Bounds.Width, "a ação principal ocupa a largura do painel");
                    Save(window, "conferir-v3-um-clique", theme);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Em_1366_por_768_as_acoes_do_painel_ficam_na_area_visivel(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = ArtboardProject.Create(temp.SubFolder("projeto"));

        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                var window = new MainWindow { Width = 1366, Height = 768, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
                    await vm.ConferenceLoading;
                    vm.Conference.SelectedRow = vm.Conference.Rows.First(r => r.RuleId == "espaco-duplo");
                    ScreenRenderer.Settle(window);

                    var view = window.GetVisualDescendants().OfType<ConferenceView>().Single();
                    AssertInsidePanelFooter(window, view, ActionButton(view, vm.Conference.FixAndNextCommand));
                    AssertInsidePanelFooter(window, view, ActionButton(view, vm.Conference.MarkOkCommand));
                    AssertInsidePanelFooter(window, view, ActionButton(view, vm.Conference.OpenLineCommand));
                    var path = RenderOutput.PathFor(ScreenRenderer.FileName("conferir-1366-um-clique", theme));
                    ScreenRenderer.Save(window, path);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static Border PanelOf(ConferenceView view) =>
        view.GetVisualDescendants().OfType<Border>().Single(b => AutomationProperties.GetName(b) == "Problema selecionado");

    private static IEnumerable<Button> PanelButtons(ConferenceView view) => PanelOf(view).GetVisualDescendants().OfType<Button>();

    private static Button ActionButton(ConferenceView view, System.Windows.Input.ICommand command) =>
        Assert.Single(PanelButtons(view), b => b.Command == command && b.IsEffectivelyVisible);

    /// <summary>O botão está na tela, dentro do painel do problema, abaixo da área que rola e acima da borda do painel.</summary>
    private static void AssertInsidePanelFooter(Window window, ConferenceView view, Button button)
    {
        var panel = PanelOf(view);
        var scroll = panel.GetVisualDescendants().OfType<ScrollViewer>().First();
        var panelBox = new Rect(panel.TranslatePoint(default, window)!.Value, panel.Bounds.Size);
        var scrollBox = new Rect(scroll.TranslatePoint(default, window)!.Value, scroll.Bounds.Size);
        var box = new Rect(button.TranslatePoint(default, window)!.Value, button.Bounds.Size);
        Assert.True(panelBox.Contains(box.TopLeft) && panelBox.Contains(box.BottomRight), "o botão cabe no painel");
        Assert.True(box.Top >= scrollBox.Bottom, "a barra de ações fica abaixo do conteúdo que rola");
        Assert.True(box.Bottom <= window.Height, "o botão está na área visível");
    }

    /// <summary>Um só botão de destaque no cabeçalho: "Corrigir N com um clique" quando há correção, e "Conferir de novo" fica neutro.</summary>
    private static void AssertHeader(ConferenceView view, ConferenceViewModel conference)
    {
        var buttons = view.GetVisualDescendants().OfType<Button>().ToList();
        var fixAll = Assert.Single(buttons, b => b.Command == conference.FixAllCommand);
        var refresh = Assert.Single(buttons, b => b.Command == conference.RefreshCommand && b.IsEffectivelyVisible);
        Assert.Contains("accent", fixAll.Classes);
        Assert.DoesNotContain("accent", refresh.Classes);
        Assert.Single(buttons, b => b.Classes.Contains("accent") && b.Classes.Contains("sm") && b.IsEffectivelyVisible);
    }

    /// <summary>A faixa do topo é de cartão, com a barra de erro à esquerda e o número em vermelho: nada de fundo vermelho cheio.</summary>
    private static void AssertBlockingBanner(Window window, ConferenceView view, ConferenceViewModel conference, ThemeVariant theme)
    {
        var banner = Assert.Single(view.GetVisualDescendants().OfType<Border>(), b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == conference.BlockingMessage);
        Assert.Equal(conference.BlockingMessage, $"{conference.BlockingCount} {conference.BlockingRest}");
        var errorBg = ColorOf("Vnf.SignalErrorBg", theme);
        var errorFg = ColorOf("Vnf.SignalErrorFg", theme);
        var probe = PixelProbe.Capture(window, banner);
        Assert.True(probe.CountInArea(new Rect(8, 2, probe.Width - 16, probe.Height - 4), errorBg, 4) < probe.Width * probe.Height / 100, "a faixa não tem fundo vermelho");
        Assert.True(probe.CountInArea(new Rect(0, 4, 3, probe.Height - 8), errorFg, 6) > probe.Height / 2, "a barra de erro desenha a borda esquerda");
        var number = banner.GetVisualDescendants().OfType<TextBlock>().SelectMany(t => t.Inlines ?? []).OfType<Run>().Single(r => r.Text == conference.BlockingCount);
        Assert.Equal(errorFg, Assert.IsType<ISolidColorBrush>(number.Foreground, exactMatch: false).Color);
    }

    private static Color ColorOf(string key, ThemeVariant theme) =>
        Application.Current!.TryGetResource(key, theme, out var value) && value is ISolidColorBrush brush ? brush.Color : throw new InvalidOperationException(key);

    /// <summary>Toda cena aparece pelo nome ou por "Cena N", nunca pelo id; os marcadores viram selo e não texto entre parênteses.</summary>
    private static void AssertSceneNamesAndChips(ConferenceView view)
    {
        var list = view.GetVisualDescendants().OfType<ListBox>().First(l => AutomationProperties.GetName(l) == "Problemas encontrados");
        var texts = list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains(texts, t => t.StartsWith("Cena ", StringComparison.Ordinal) && t.Contains("· linha", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.StartsWith("Manhã de chuva · linha", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.Contains("yst000", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.Contains("(virar a página)", StringComparison.Ordinal));
        var chips = list.GetVisualDescendants().OfType<MarkerChip>().Where(c => c.IsEffectivelyVisible).ToList();
        Assert.True(chips.Count >= 1, "os marcadores da lista são selos");
        Assert.All(chips, chip => Assert.True(chip.Bounds.Height >= 18));
    }

    private static void Save(MainWindow window, string name, ThemeVariant theme)
    {
        ScreenRenderer.Settle(window);
        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
        ScreenRenderer.Save(window, path);
        using var png = new Bitmap(path);
        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
    }

    /// <summary>As linhas do artboard "Conferir", escritas para ele: nenhum arquivo ou texto de jogo.</summary>
    private static class ArtboardProject
    {
        public static string Create(string parent)
        {
            var folder = ProjectFolder.Create(parent, new ProjectConfig(
                DemoProject.Name,
                new GameConfig(@"D:\Jogos\MinhaVN", "MinhaVN.exe"),
                new EngineConfig("yuris", 500, "0xD36FAC96"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));
            folder.SaveFormatting(FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 40, MaxLines: 3) });
            folder.SaveGlossary([new GlossaryTerm("先輩", "veterano"), new GlossaryTerm("担任の先生", "professor responsável")]);
            folder.SaveScriptNames(ScriptNames.From([new ScriptNameEntry("yst00013", null, "Manhã de chuva"), new ScriptNameEntry("yst00014", null, "Hora do almoço")]));
            folder.SaveText("yst00012", [Line("yst00012", 42, @"「本当に、いいの？」\p", "De verdade, tudo bem?")]);
            folder.SaveText(
                "yst00013",
                [
                    Line("yst00013", 43, @"「……いいのか？\n濡れるぞ、お前」\p", "…Tem certeza?  Você vai se molhar."),
                    Line("yst00013", 44, @"「平気平気！　ほら、行こう！」\p", @"Tranquilo! Vamos, anda !\p"),
                    Line("yst00013", 45, @"担任の先生が腕を組んで立っていた。\p", @"O nosso professor responsável já estava de pé, esperando por nós dois de braços cruzados na frente do portão da escola.\p"),
                ]);
            folder.SaveText(
                "yst00014",
                [
                    Line("yst00014", 128, @"「了解」\p", @"Beleza.\p"),
                    Line("yst00014", 129, @"「了解」\p", @"Entendido.\p"),
                    Line("yst00014", 130, @"「了解」\p", @"Certo.\p"),
                ]);
            folder.SaveText("yst00020", [Line("yst00020", 311, @"「先輩、待ってください！」\p", @"Sênior, espera aí!\p")]);
            folder.SaveText(
                "yst00031",
                [
                    Line("yst00031", 102, @"「『暁の書』って知ってる？」\p", @"Você conhece o Livro da Aurora ★?\p"),
                    new TextUnit("yst00031:000103", TextUnitKind.Dialogue, null, "「まだだよ」\\p", null, TranslationStatus.Untranslated),
                    new TextUnit("yst00031:000104", TextUnitKind.Dialogue, null, "「行こう」\\p", null, TranslationStatus.Untranslated),
                ]);
            return folder.Root;
        }

        private static TextUnit Line(string key, int number, string source, string translation) =>
            new($"{key}:{number:D6}", TextUnitKind.Dialogue, null, source, translation, TranslationStatus.Translated);
    }
}
