using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Review;
using VNForge.AI.Settings;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// A segunda passada de editor (#532) montada de verdade nos dois temas, a 1440×1000 e a 1366×768: a grade com o ícone Sparkle, o cartão Aceitar / Ignorar / Outra no inspetor e
/// no Modo revisão, e a caixa da passada na janela "Traduzir com IA". Os PNG vão para <c>VNFORGE_RENDER_DIR</c>, ao lado do quadro <c>#issue-532</c>.
/// O cartão não pode cortar texto nem sair da janela.
/// </summary>
public sealed class EditorPassRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, int, int> Cases => new()
    {
        { "Dark", 1440, 1000 },
        { "Light", 1440, 1000 },
        { "Dark", 1366, 768 },
        { "Light", 1366, 768 },
    };

    private static string CreateProject(TempFolder temp, bool pending)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Amaoto no Hakoniwa", TranslationStatus.Translated);
        var folder = ProjectFolder.Open(root);
        TextUnit Line(int n, string source, string? translation, TranslationStatus status) => new($"yst00001:{n:D6}", TextUnitKind.Dialogue, "美咲", source, translation, status);
        folder.SaveText("yst00001", pending
            ?
            [
                Line(0, "「相沢、佐倉。二人そろって五分遅刻だ」", null, TranslationStatus.Untranslated),
                Line(1, "「平気平気！ほら、行こう！」", null, TranslationStatus.Untranslated),
            ]
            :
            [
                Line(0, "「相沢、佐倉。二人そろって五分遅刻だ」", "Aizawa, Sakura. Os dois, cinco minutos atrasados.", TranslationStatus.Machine),
                Line(1, "「平気平気！ほら、行こう！」", "Tranquilo! Vamos, anda!", TranslationStatus.Machine),
                Line(2, "「傘、忘れたの？」", "Esqueceu o guarda-chuva?", TranslationStatus.Machine),
                Line(3, "「うん、まあね」", "Uhum, mais ou menos.", TranslationStatus.Machine),
            ]);
        if (!pending)
        {
            EditorSuggestionsFile.Save(folder, EditorSuggestionsFile.Empty.ReplacingScript("yst00001",
            [
                new("yst00001", "yst00001:000000", EditorSuggestionKind.Fluency, "Aizawa, Sakura. Os dois, cinco minutos atrasados.", "Aizawa, Sakura. Vocês dois, cinco minutos atrasados.", "Soa mais natural em português.", Now),
                new("yst00001", "yst00001:000002", EditorSuggestionKind.Voice, "Esqueceu o guarda-chuva?", "Esqueceu o guarda-chuva de novo?", "A fala dela é mais provocadora no original.", Now),
                new("yst00001", "yst00001:000003", EditorSuggestionKind.Consistency, "Uhum, mais ou menos.", "Uhum, mais ou menos isso.", "A cena usa \"mais ou menos isso\" nas outras falas.", Now),
            ]));
        }

        return root;
    }

    private async Task<int> InWindow(string themeName, int width, int height, bool pending, Func<Window, MainWindowViewModel, Task> inspect)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp, pending);
        return await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var settings = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(config.SubFolder("ajustes")), _ => null);
                var editor = new EditorViewModel(clock: new FixedClock(Now)) { EditorSuggestionAnother = (s, _) => Task.FromResult<EditorSuggestion?>(s) };
                var vm = new MainWindowViewModel(start, new ResolvedApiKey("chave", ApiKeySource.Vault), settings: settings) { Editor = editor };
                var window = new MainWindow { Width = width, Height = height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await editor.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                    vm.Settings.ExchangeRateText = "5,40";
                    await inspect(window, vm);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void Save(Window window, string name, ThemeVariant theme, int width) =>
        ScreenRenderer.Save(window, RenderOutput.PathFor($"{ScreenRenderer.FileName(name, theme)[..^4]}-{width}.png"));

    private static void AssertInside(Window window, Control control, string what, bool scrollable = false)
    {
        Assert.True(control.IsEffectivelyVisible, $"{what} não está visível.");
        Assert.True(control.Bounds.Height > 40, $"{what} tem {control.Bounds.Height} px de altura.");
        var origin = control.TranslatePoint(default, window) ?? default;

        // O inspetor rola quando a janela é baixa (como com o cartão da memória): o que vale é haver uma rolagem que leve até o cartão.
        var scroll = control.FindAncestorOfType<ScrollViewer>();
        Assert.True(origin.X >= 0 && origin.Y >= 0 && origin.X + control.Bounds.Width <= window.Bounds.Width
            && (origin.Y + control.Bounds.Height <= window.Bounds.Height || (scrollable && scroll is { Extent.Height: var extent, Viewport.Height: var viewport } && extent > viewport)),
            $"{what} ({origin.X:0},{origin.Y:0} {control.Bounds.Width:0}x{control.Bounds.Height:0}) sai da janela de {window.Bounds.Width:0}x{window.Bounds.Height:0}.");
        Assert.Empty(ClippedTextHeadlessTests.FindClipped(control));
    }

    private static void AssertButtons(EditorSuggestionCardView card)
    {
        foreach (var name in new[] { "AcceptButton", "IgnoreButton", "OtherButton" })
        {
            var button = card.FindControl<Button>(name)!;
            Assert.True(button.IsEffectivelyVisible && button.Bounds.Width > 40, $"O botão {name} não aparece.");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task O_inspetor_mostra_o_cartao_da_passada_e_a_grade_o_icone(string themeName, int width, int height)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await InWindow(themeName, width, height, pending: false, async (window, vm) =>
        {
            await vm.Editor.GoToLineAsync("yst00001", "yst00001:000000");
            ScreenRenderer.Settle(window);

            var icons = window.GetVisualDescendants().OfType<LineRow>().Where(r => r.IsEffectivelyVisible && r.HasEditorSuggestion)
                .Select(r => r.GetVisualDescendants().OfType<PathIcon>().Single(i => i.Name == "PART_Suggestion")).ToList();
            var rows = window.GetVisualDescendants().OfType<LineRow>().ToList();
            Assert.True(icons.Count == 3, $"linhas na grade: {rows.Count}, visíveis: {rows.Count(r => r.IsEffectivelyVisible)}, com sugestão: {rows.Count(r => r.HasEditorSuggestion)}, no VM: {vm.Editor.Lines.Count(l => l.HasEditorSuggestion)}, ícones: {icons.Count}.");
            Assert.All(icons, i => Assert.True(i.IsEffectivelyVisible && i.Bounds.Width >= 16));
            Assert.Equal(1, window.GetVisualDescendants().OfType<LineRow>().Count(r => r.IsEffectivelyVisible && !r.HasEditorSuggestion));

            var inspector = window.GetVisualDescendants().OfType<LineDetailView>().First(v => v.IsEffectivelyVisible);
            var card = inspector.GetVisualDescendants().OfType<EditorSuggestionCardView>().Single(c => c.IsEffectivelyVisible);
            AssertInside(window, card, "O cartão da passada", scrollable: true);
            AssertButtons(card);
            Assert.Equal("fluidez · 1 de 3", vm.Editor.Detail!.EditorSuggestionChip);
            Save(window, "editor-passada", theme, width);
        });
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task O_modo_revisao_mostra_o_cartao_abaixo_da_traducao_e_antes_da_fala_seguinte(string themeName, int width, int height)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await InWindow(themeName, width, height, pending: false, async (window, vm) =>
        {
            vm.Editor.SelectedScript ??= vm.Editor.Scripts.First();
            vm.Editor.StartReview();
            ScreenRenderer.Settle(window);

            var review = window.GetVisualDescendants().OfType<ReviewModeView>().Single(v => v.IsEffectivelyVisible);
            var card = review.GetVisualDescendants().OfType<EditorSuggestionCardView>().Single(c => c.IsEffectivelyVisible);
            AssertInside(window, card, "O cartão da passada");
            AssertButtons(card);
            Assert.Equal("Passada de editor · fluidez", vm.Editor.Review!.Detail!.EditorSuggestionReviewTitle);

            var next = review.FindControl<Control>("NextBlock")!;
            var cardTop = (card.TranslatePoint(default, window) ?? default).Y;
            var nextTop = (next.TranslatePoint(default, window) ?? default).Y;
            Assert.True(cardTop < nextTop, "O cartão tem de ficar antes de 'Fala seguinte'.");
            Save(window, "revisao-passada", theme, width);
        });
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_janela_Traduzir_com_IA_traz_a_caixa_da_passada_marcada_com_o_preco(string themeName, int width, int height)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await InWindow(themeName, width, height, pending: true, async (window, vm) =>
        {
            vm.Editor.SelectedScript ??= vm.Editor.Scripts.First();
            vm.Editor.TranslatePendingCommand.Execute(null);
            await vm.TranslateOpening;
            ScreenRenderer.Settle(window);

            var surface = OverlayLayer.GetOverlayLayer(window)!.Children.OfType<Control>().Single(c => c.GetVisualDescendants().OfType<QualityCard>().Any());
            var card = surface.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "EditorPassCard" && b.IsEffectivelyVisible);
            var check = card.GetVisualDescendants().OfType<CheckBox>().Single();
            Assert.True(check.IsChecked);
            Assert.StartsWith("+ R$", vm.Translate.EditorPassPriceText);
            var price = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == vm.Translate.EditorPassPriceText);
            Assert.True(price.IsEffectivelyVisible);
            Assert.Empty(ClippedTextHeadlessTests.FindClipped(card));
            Save(window, "traduzir-passada", theme, width);
        });
    }
}
