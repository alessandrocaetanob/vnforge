using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Claude;
using VNForge.AI.Explain;
using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.App.Controls;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Memory;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// A memória de tradução entre projetos (#537) a 1440×1000 nos dois temas: o cartão no inspetor do Editor (exata em selo de aprovado, aproximada em selo de IA, "Na cena"
/// recolhido) e a aba "Meu glossário" com "No projeto", "Difere do projeto" e "Usar no projeto". Os PNG seguem <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class TranslationMemoryRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private const string Frase = "「相沢、佐倉。二人そろって五分遅刻だ」";

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static string CreateProject(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Amaoto no Hakoniwa", TranslationStatus.Translated);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001",
        [
            new("yst00001:000000", TextUnitKind.Dialogue, "健太", "「えへへ……すみません」", "Ehehe... Desculpa.", TranslationStatus.Translated),
            new("yst00001:000001", TextUnitKind.Dialogue, "美咲", Frase, "Aizawa, Sakura. Os dois, cinco minutos atrasados.", TranslationStatus.Machine),
            new("yst00001:000002", TextUnitKind.Dialogue, "健太", "「はい」", "Sim.", TranslationStatus.Translated),
        ]);
        // O cabeçalho do inspetor do quadro: retrato, nome, papel no jogo (do VNDB) e jeito de falar da Misaki.
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, Tone: "Professora, rígida", Color: "rose", VndbId: "c100")]);
        folder.SaveVndbLink(new VndbLink("v100", "Amaoto no Hakoniwa", null, null, [new VndbLinkedCharacter("c100", "Misaki", "美咲", [], CharacterGender.Female, null, "side")]));
        // O quadro mostra "Cabe · 2 de 3 linhas": o perfil do jogo quebra em 40 colunas e 3 linhas.
        folder.SaveFormatting(FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 40, MaxLines: 3, CharWidth: CharWidthMode.Uniform) });
        // O quadro mostra seis termos do pessoal: três já no projeto (um difere), dois para usar; e três do projeto para guardar.
        folder.SaveGlossary(
        [
            new GlossaryTerm("お兄ちゃん", "mano"), new GlossaryTerm("先輩", "senpai"), new GlossaryTerm("ふふっ", "hehe"),
            new GlossaryTerm("遅刻", "atraso"), new GlossaryTerm("傘", "guarda-chuva"), new GlossaryTerm("校門", "portão da escola"),
        ]);
        return root;
    }

    private static TranslationMemory Fill(TempFolder config)
    {
        var memory = new TranslationMemory(config.Path);
        var near = new TextUnit("b:000001", TextUnitKind.Dialogue, null, "「相沢、二人そろって五分遅刻だ」", "Aizawa, os dois, cinco minutos atrasados.", TranslationStatus.Approved);
        var exact = new TextUnit("a:000001", TextUnitKind.Dialogue, null, Frase, "Aizawa, Sakura. Vocês dois, cinco minutos atrasados.", TranslationStatus.Approved);
        memory.Record(TranslationMemory.KeyOf(Path.Combine(config.Path, "estrela")), "Hoshizora no Kakera", exact, Now.AddDays(-56));
        memory.Record(TranslationMemory.KeyOf(Path.Combine(config.Path, "verao")), "Natsu no Shirabe", near, Now.AddDays(-38));
        return memory;
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_cartao_da_memoria_no_inspetor_do_Editor(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp);

        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                // Com provedor de IA o "Entender o japonês" e o "Usar sugestão" ficam ativos, como no quadro.
                var suggester = new LineSuggester(() => new FakeTranslationProvider(u => (u.Translation ?? u.Source).Replace("Os dois", "Vocês dois")));
                var editor = new EditorViewModel(clock: new FixedClock(Now), suggester: suggester) { Memory = Fill(config), Explanations = new ExplicacaoDeExemplo() };
                var vm = new MainWindowViewModel(start) { Editor = editor };
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await editor.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                    await editor.GoToLineAsync("yst00001", "yst00001:000001");
                    await editor.Detail!.MemoryLoading;
                    await editor.Detail.SuggestCommand.ExecuteAsync(null);
                    ScreenRenderer.Settle(window);

                    var card = Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "MemoryFragment" && b.IsEffectivelyVisible);
                    Assert.True(card.Bounds.Height > 100, $"O cartão tem {card.Bounds.Height} px de altura.");
                    var buttons = card.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Content as string == "Usar").ToList();
                    Assert.Equal(2, buttons.Count);
                    Assert.All(buttons, b => Assert.DoesNotContain("accent", b.Classes));
                    // O selo de porcentagem é o chip do tema comum: Vnf.PillHeight (20 px), em Noto Sans (não mono).
                    var pills = card.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("memory-pill") && b.IsEffectivelyVisible).ToList();
                    Assert.Equal(2, pills.Count);
                    Assert.All(pills, b => Assert.Equal(20, b.Bounds.Height));
                    Assert.True(editor.Detail.IsSceneCollapsed);
                    var header = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "HeaderFragment" && g.IsEffectivelyVisible);
                    var headerTexts = header.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
                    Assert.Contains("Misaki", headerTexts);
                    Assert.Contains("Secundária · Professora, rígida", headerTexts);
                    Assert.Contains("yst00001:000001 · Diálogo", headerTexts);
                    Assert.Equal(64, Assert.Single(header.GetVisualDescendants().OfType<Portrait>()).Bounds.Width);
                    // Histórico e Mais: ícones de 12 px, apagados, como no quadro.
                    var headerIcons = header.GetVisualDescendants().OfType<PathIcon>().Where(i => i.IsEffectivelyVisible).ToList();
                    Assert.Equal(2, headerIcons.Count);
                    Assert.All(headerIcons, i => Assert.Equal(12, i.Width));
                    Assert.Equal("2 falas antes", editor.Detail.SceneCountText);
                    // A caixa do jogo é o bloco liso do quadro: 68 px de bloco mais os 10 px da etiqueta do nome que sobem sobre a borda, com 2 linhas de texto, na largura toda do inspetor, e sem divisor sobre o "Aprovar e próxima".
                    var box = window.GetVisualDescendants().OfType<GameTextbox>().Single(g => g.IsEffectivelyVisible);
                    Assert.Equal(78, box.Bounds.Height);
                    Assert.Equal(((Control)box.Parent!).Bounds.Width, box.Bounds.Width);
                    Assert.Equal(2, box.Rows.Count);
                    Assert.Equal(default, window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ActionsBar").BorderThickness);
                    Assert.All(window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Name == "ApproveButton" || b.Classes.Contains("use-in-row")), b => Assert.True(b.IsEnabled));
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("editor-memoria", theme)));
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
    public async Task Renderiza_a_aba_Meu_glossario(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp);

        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var personal = new PersonalGlossary(config.Path);
                personal.Upsert(new PersonalTerm("お兄ちゃん", "mano", "irmão mais velho, tom de carinho", ["a", "b", "c", "d"]));
                personal.Upsert(new PersonalTerm("先輩", "senpai", "manter em romaji", ["a", "b", "c", "d", "e", "f"]));
                personal.Upsert(new PersonalTerm("ドキドキ", "tum-tum", "batida do coração, onomatopeia", ["a", "b", "c"]));
                personal.Upsert(new PersonalTerm("ふふっ", "hehe", "risada baixa", ["a", "b", "c", "d", "e"]));
                personal.Upsert(new PersonalTerm("すごい", "incrível", "sem gíria", ["a", "b", "c", "d"]));
                personal.Upsert(new PersonalTerm("遅刻", "chegar tarde", "no projeto o termo é \"atraso\"", ["a", "b"]));
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                vm.Glossary.PersonalStore = personal;
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Glossary);
                    await vm.Glossary.Loading;
                    vm.Glossary.SelectedTab = GlossaryTab.Personal;
                    ScreenRenderer.Settle(window);

                    var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
                    Assert.Equal(2, buttons.Count(b => b.Content as string == "Usar no projeto"));
                    // O quadro não desenha botões de ícone nas linhas: editar e tirar vão para o menu da linha.
                    Assert.DoesNotContain(buttons, b => AutomationProperties.GetName(b) is { } name && (name.StartsWith("Editar ", StringComparison.Ordinal) || name.StartsWith("Tirar ", StringComparison.Ordinal)));
                    // A página abre direto nas abas: sem o título "Glossário" nem a frase de apresentação.
                    var page = window.GetVisualDescendants().OfType<GlossaryView>().Single();
                    Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && (t.Text == "Glossário" || (t.Text?.StartsWith("Nomes e termos fixos", StringComparison.Ordinal) ?? false)));
                    Assert.Equal(3, buttons.Count(b => b.Content as string == "Guardar"));
                    // O banner do quadro: título em peso regular, corpo em cor secundária, entrelinha de 18 e 78 px de altura.
                    var banner = Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("infobar") && b.IsEffectivelyVisible);
                    var bannerTexts = banner.GetVisualDescendants().OfType<TextBlock>().ToList();
                    Assert.Equal(PersonalGlossaryViewModel.BannerText, bannerTexts[^1].Text);
                    Assert.All(bannerTexts, t => Assert.Equal(FontWeight.Normal, t.FontWeight));
                    Assert.Contains("secondary", bannerTexts[^1].Classes);
                    Assert.Equal(78, banner.Bounds.Height);
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "No projeto");
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Difere do projeto");
                    // Os dois cartões vão até o pé da página, e a nota do rodapé fica na base do cartão da tabela.
                    var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "PageScroll");
                    var table = Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("panel") && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Termos que valem em toda VN"));
                    var bottom = Avalonia.VisualExtensions.TranslatePoint(table, new Avalonia.Point(0, table.Bounds.Height), scroller)!.Value.Y;
                    Assert.True(bottom >= scroller.Viewport.Height - 14 - 1, $"O cartão da tabela termina em {bottom} px e a área visível tem {scroller.Viewport.Height} px.");
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("glossario-pessoal", theme)));
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private sealed class ExplicacaoDeExemplo : ILineExplanations
    {
        public bool IsAvailable => true;

        public string PriceText => "menos de R$ 0,01";

        public Task<LineExplanation> ExplainAsync(string projectRoot, string source, CancellationToken ct) =>
            Task.FromResult(new LineExplanation([new ExplainedPart("相沢", "Aizawa (sobrenome)")], ClaudeModels.Haiku, FromCache: false));
    }
}
