using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Glossary;
using VNForge.Core.Memory;
using VNForge.Core.Projects;
using VNForge.Core.Text;

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
        folder.SaveGlossary([new GlossaryTerm("先輩", "senpai"), new GlossaryTerm("遅刻", "atraso"), new GlossaryTerm("傘", "guarda-chuva")]);
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
                var editor = new EditorViewModel(clock: new FixedClock(Now)) { Memory = Fill(config) };
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
                    ScreenRenderer.Settle(window);

                    var card = Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "MemoryFragment" && b.IsEffectivelyVisible);
                    Assert.True(card.Bounds.Height > 100, $"O cartão tem {card.Bounds.Height} px de altura.");
                    var buttons = card.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Content as string == "Usar").ToList();
                    Assert.Equal(2, buttons.Count);
                    Assert.All(buttons, b => Assert.DoesNotContain("accent", b.Classes));
                    Assert.True(editor.Detail.IsSceneCollapsed);
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
                    Assert.Equal(2, buttons.Count(b => b.Content as string == "Guardar"));
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "No projeto");
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Difere do projeto");
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
}
