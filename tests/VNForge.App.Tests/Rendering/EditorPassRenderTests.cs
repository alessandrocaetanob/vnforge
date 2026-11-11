using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Explain;
using VNForge.AI.Review;
using VNForge.AI.Settings;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
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

    private const string LongText = "O nosso professor responsável já estava de pé, esperando por nós dois de braços cruzados na frente do portão da escola, debaixo da chuva.";
    private const string ShortText = "O nosso professor responsável já estava de pé, esperando por nós de braços cruzados na frente do portão, debaixo da chuva.";

    private enum Scenario
    {
        /// <summary>O Editor com sugestões em três linhas, como o quadro "Editor com sugestão na linha".</summary>
        Editor,

        /// <summary>A cena do quadro "Modo revisão com sugestão": a segunda de três falas, de Kenta, passa uma linha da caixa de três.</summary>
        Review,

        /// <summary>Linhas sem tradução, com glossário e voz dos personagens, para a janela "Traduzir com IA".</summary>
        Pending,
    }

    private static string CreateProject(TempFolder temp, Scenario scenario)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Amaoto no Hakoniwa", TranslationStatus.Translated);
        var folder = ProjectFolder.Open(root);
        folder.SaveFormatting(FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: scenario == Scenario.Review ? 40 : 30, MaxLines: 3, CharWidth: CharWidthMode.Uniform) });
        // As cores do quadro: Misaki em rosa e Kenta em verde.
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "animada, provoca o Kenta") { Color = "rose" }, new CharacterEntry("健太", "Kenta", CharacterGender.Male) { Color = "green" }]);
        TextUnit Line(int n, string speaker, string source, string? translation, TranslationStatus status) => new($"yst00001:{n:D6}", TextUnitKind.Dialogue, speaker, source, translation, status);
        switch (scenario)
        {
            case Scenario.Pending:
                AiUsageLog.TryRecord(folder, new AiUsageTotals(1_500_000, 600_000, 0, 0, 26.40m, 1));
                folder.SaveGlossary([new("魔法", "magia"), new("学校", "escola"), new("傘", "guarda-chuva"), new("先生", "professor")]);
                folder.SaveText("yst00001",
                [
                    Line(0, "美咲", "「相沢、佐倉。二人そろって五分遅刻だ」", null, TranslationStatus.Untranslated),
                    Line(1, "美咲", "「平気平気！ほら、行こう！」", null, TranslationStatus.Untranslated),
                    // Um capítulo de verdade: com centenas de linhas o pedido deixa de ser centavos e a barra do custo mostra o trecho azul do pedido depois do gasto roxo.
                    .. Enumerable.Range(2, 1500).Select(n => Line(n, n % 3 == 0 ? "健太" : "美咲", "「今日はいい天気ですね、先生。傘は持ってきましたか？」", null, TranslationStatus.Untranslated)),
                ]);
                break;
            case Scenario.Review:
                folder.SaveText("yst00001",
                [
                    Line(0, "美咲", @"「平気平気！　ほら、行こう！」\p", @"Tranquilo! Vamos, anda!\p", TranslationStatus.Approved),
                    Line(1, "健太", @"担任の先生が腕を組んで立っていた。\p", LongText + @"\p", TranslationStatus.Machine),
                    Line(2, "美咲", @"「うん」\p", @"Aham.\p", TranslationStatus.Machine),
                ]);
                EditorSuggestionsFile.Save(folder, EditorSuggestionsFile.Empty.ReplacingScript("yst00001",
                [
                    new("yst00001", "yst00001:000001", EditorSuggestionKind.Voice, LongText + @"\p", ShortText + @"\p", "Tira \"dois de\" para caber na caixa e mantém a ordem do original.", Now),
                ]));
                break;
            default:
                folder.SaveText("yst00001",
                [
                    Line(0, "美咲", "「相沢、佐倉。二人そろって五分遅刻だ」", "Aizawa, Sakura. Os dois, cinco minutos atrasados.", TranslationStatus.Machine),
                    Line(1, "美咲", "「平気平気！ほら、行こう！」", "Tranquilo! Vamos, anda!", TranslationStatus.Machine),
                    Line(2, "美咲", "「傘、忘れたの？」", "Esqueceu o guarda-chuva?", TranslationStatus.Machine),
                    Line(3, "美咲", "「うん、まあね」", "Uhum, mais ou menos.", TranslationStatus.Machine),
                ]);
                EditorSuggestionsFile.Save(folder, EditorSuggestionsFile.Empty.ReplacingScript("yst00001",
                [
                    new("yst00001", "yst00001:000000", EditorSuggestionKind.Fluency, "Aizawa, Sakura. Os dois, cinco minutos atrasados.", "Aizawa, Sakura. Vocês dois, cinco minutos atrasados.", "Soa mais natural em português.", Now),
                    new("yst00001", "yst00001:000002", EditorSuggestionKind.Voice, "Esqueceu o guarda-chuva?", "Esqueceu o guarda-chuva de novo?", "A fala dela é mais provocadora no original.", Now),
                    new("yst00001", "yst00001:000003", EditorSuggestionKind.Consistency, "Uhum, mais ou menos.", "Uhum, mais ou menos isso.", "A cena usa \"mais ou menos isso\" nas outras falas.", Now),
                ]));
                break;
        }

        return root;
    }

    private async Task<int> InWindow(string themeName, int width, int height, Scenario scenario, Func<Window, MainWindowViewModel, Task> inspect)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp, scenario);
        return await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var settings = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(config.SubFolder("ajustes")), _ => null);
                var editor = new EditorViewModel(clock: new FixedClock(Now)) { EditorSuggestionAnother = (s, _) => Task.FromResult<EditorSuggestion?>(s), Explanations = new ExplicacaoDeExemplo() };
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
                    vm.Settings.SpendLimitText = "300";
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

    private sealed class ExplicacaoDeExemplo : ILineExplanations
    {
        public bool IsAvailable => true;

        public string PriceText => string.Empty;

        public Task<LineExplanation> ExplainAsync(string projectRoot, string source, CancellationToken ct) =>
            Task.FromResult(new LineExplanation([new ExplainedPart("今日は", "hoje")], ClaudeModels.Haiku, FromCache: false));
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
        await InWindow(themeName, width, height, Scenario.Editor, async (window, vm) =>
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
            // O inspetor inteiro (350 px) larga a tecla do botão mas mantém a faísca do "Usar sugestão", e a coluna tem os 350 px do quadro (a barra de rolagem fica na folga de 12 px ao lado).
            if (!inspector.Classes.Contains("tabbed"))
            {
                Assert.DoesNotContain("tight", inspector.Classes);
                Assert.Equal(inspector.Bounds.Width, inspector.FindControl<Control>("WideStack")!.Bounds.Width);
            }
            Save(window, "editor-passada", theme, width);
            if (!inspector.Classes.Contains("tabbed"))
            {
                // Quadro (#580): caixa do Original de 84 px com o japonês em 20, cartão da passada 9 px abaixo dela e "Entender o japonês" com contorno.
                var box = inspector.FindControl<Control>("OriginalBox")!;
                Assert.Equal(20, inspector.FindControl<TextBlock>("SourceText")!.FontSize);
                Assert.Equal(84, box.Bounds.Height, 0.5);
                var gap = (card.TranslatePoint(default, window) ?? default).Y - (box.TranslatePoint(new Point(0, box.Bounds.Height), window) ?? default).Y;
                Assert.Equal(9, gap, 0.5);
                var explain = Assert.Single(inspector.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == vm.Editor.Detail!.ToggleExplainCommand);
                Assert.DoesNotContain("subtle", explain.Classes);
                Assert.NotEqual(0, ((ISolidColorBrush)explain.BorderBrush!).Color.A);
                Assert.All(inspector.GetVisualDescendants().OfType<OverlineText>().Where(o => o.IsEffectivelyVisible && o.Text is "Original" or "Tradução"), o => Assert.Equal(FontWeight.Normal, o.FontWeight));

                // Uma fala de uma linha só (「うん、まあね」) não encolhe a caixa: o piso é de duas linhas.
                await vm.Editor.GoToLineAsync("yst00001", "yst00001:000003");
                ScreenRenderer.Settle(window);
                Assert.Single(inspector.FindControl<TextBlock>("SourceText")!.TextLayout.TextLines);
                Assert.Equal(84, inspector.FindControl<Control>("OriginalBox")!.Bounds.Height, 0.5);
            }

            // O Roteiro e o Buscar do cabeçalho são subtle: sem contorno e em semibold.
            var script = window.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "DensityToggle" && b.IsEffectivelyVisible);
            var search = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SearchButton" && b.IsEffectivelyVisible);
            Assert.All(new TemplatedControl[] { script, search }, b => Assert.Equal(0, ((ISolidColorBrush)b.BorderBrush!).Color.A));
            Assert.All(new TemplatedControl[] { script, search }, b => Assert.Equal(FontWeight.SemiBold, b.FontWeight));
        });
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task O_modo_revisao_mostra_o_cartao_abaixo_da_traducao_e_antes_da_fala_seguinte(string themeName, int width, int height)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await InWindow(themeName, width, height, Scenario.Review, async (window, vm) =>
        {
            vm.Editor.SelectedScript ??= vm.Editor.Scripts.First();
            vm.Editor.StartReview();
            ScreenRenderer.Settle(window);

            var review = window.GetVisualDescendants().OfType<ReviewModeView>().Single(v => v.IsEffectivelyVisible);
            var card = review.GetVisualDescendants().OfType<EditorSuggestionCardView>().Single(c => c.IsEffectivelyVisible);
            AssertInside(window, card, "O cartão da passada");
            Assert.Equal("Passada de editor · voz", vm.Editor.Review!.Detail!.EditorSuggestionReviewTitle);

            // Como no quadro: só "Aceitar" e "Ignorar", sem "Outra".
            Assert.True(card.FindControl<Button>("AcceptButton")!.IsEffectivelyVisible);
            Assert.True(card.FindControl<Button>("IgnoreButton")!.IsEffectivelyVisible);
            Assert.False(card.FindControl<Button>("OtherButton")!.IsEffectivelyVisible);
            Assert.True(card.FindControl<TextBlock>("Reason")!.IsEffectivelyVisible);
            Assert.EndsWith("Nada muda até você aceitar.", card.FindControl<TextBlock>("Reason")!.Text);

            var next = review.FindControl<Control>("NextBlock")!;
            var cardTop = (card.TranslatePoint(default, window) ?? default).Y;
            var nextTop = (next.TranslatePoint(default, window) ?? default).Y;
            Assert.True(cardTop < nextTop, "O cartão tem de ficar antes de 'Fala seguinte'.");
            if (height >= 1000)
            {
                Assert.True(review.FindControl<Control>("PreviousBlock")!.IsEffectivelyVisible, "A fala anterior da cena aparece.");
            }


            // O palco tem 360 px e a caixa do jogo fica centrada nele; sob o palco vai só o aviso de que a linha ainda não cabe.
            var stage = review.FindControl<ReviewStage>("Stage")!;
            Assert.Equal(360, stage.Bounds.Height, 1);
            var box = review.FindControl<GameTextbox>("StageBox")!;
            var stageMid = (stage.TranslatePoint(default, window) ?? default).Y + (stage.Bounds.Height / 2);
            var boxMid = (box.TranslatePoint(default, window) ?? default).Y + (box.Bounds.Height / 2);
            Assert.InRange(boxMid - stageMid, -8, 8);

            // O selo "virar a página" fica na mesma fileira de "Entender o japonês", sob o original.
            var chip = Assert.Single(review.GetVisualDescendants().OfType<MarkerChip>(), c => c.IsEffectivelyVisible);
            var chevron = Assert.Single(chip.GetVisualDescendants().OfType<PathIcon>(), i => i.Name == "PART_Triangle");
            Assert.True(chevron.IsEffectivelyVisible && chevron.Width == 12, "o selo do modo revisão mostra o chevron do tema");
            var explain = Assert.Single(review.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == vm.Editor.Review.Detail!.ToggleExplainCommand);
            var chipMid = (chip.TranslatePoint(default, window) ?? default).Y + (chip.Bounds.Height / 2);
            var explainMid = (explain.TranslatePoint(default, window) ?? default).Y + (explain.Bounds.Height / 2);
            Assert.InRange(chipMid - explainMid, -4, 4);
            Assert.True((chip.TranslatePoint(default, window) ?? default).X < (explain.TranslatePoint(default, window) ?? default).X, "o selo vem antes do botão");

            var notice = review.FindControl<Border>("SuggestionFitNotice")!;
            Assert.True(notice.IsEffectivelyVisible);
            Assert.Contains("Ainda não cabe na caixa do jogo", notice.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains(notice.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is { } text && text.StartsWith("Passa 1 linha: a caixa tem 3 linhas. A sugestão economiza ", StringComparison.Ordinal));
            Assert.False(review.FindControl<Control>("LineChecks")!.IsEffectivelyVisible);
            Assert.DoesNotContain(review.GetVisualDescendants().OfType<RewriteBar>(), b => b.IsEffectivelyVisible);
            Save(window, "revisao-passada", theme, width);
            // Quadro (#580): o bloco das colunas começa 14 px abaixo do topo, e os dois botões do cartão têm contorno de 1 px, como os outros secundários.
            var topBar = review.FindControl<Control>("TopBar")!;
            var body = review.FindControl<Control>("Body")!;
            Assert.Equal(14, (body.TranslatePoint(default, window) ?? default).Y - (topBar.TranslatePoint(new Point(0, topBar.Bounds.Height), window) ?? default).Y, 0.5);
            var another = Assert.Single(review.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == vm.Editor.Review.Detail!.SuggestCommand);
            foreach (var outlined in new[] { explain, another })
            {
                Assert.DoesNotContain("subtle", outlined.Classes);
                Assert.NotEqual(0, ((ISolidColorBrush)outlined.BorderBrush!).Color.A);
            }
            Assert.All(review.GetVisualDescendants().OfType<OverlineText>().Where(o => o.IsEffectivelyVisible && o.Text is "Original" or "Tradução"), o => Assert.Equal(FontWeight.Normal, o.FontWeight));
        });
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Com_a_traducao_vazia_o_inspetor_mostra_Reescrever_com_um_toque_inteiro(string themeName, int width, int height)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await InWindow(themeName, width, height, Scenario.Pending, async (window, vm) =>
        {
            await vm.Editor.GoToLineAsync("yst00001", "yst00001:000000");
            ScreenRenderer.Settle(window);

            var inspector = window.GetVisualDescendants().OfType<LineDetailView>().First(v => v.IsEffectivelyVisible);
            var bar = inspector.GetVisualDescendants().OfType<RewriteBar>().FirstOrDefault(b => b.IsEffectivelyVisible);
            Assert.True(bar is not null || width < 1400, "O inspetor larga não mostra os chips de reescrever.");
            Assert.Empty(ClippedTextHeadlessTests.FindClipped(inspector));
            Save(window, "editor-passada-vazia", theme, width);
        });
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_janela_Traduzir_com_IA_traz_a_caixa_da_passada_marcada_com_o_preco(string themeName, int width, int height)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await InWindow(themeName, width, height, Scenario.Pending, async (window, vm) =>
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
            DialogProbe.AssertCentered(window, 780, showsClose: true);
            Save(window, "traduzir-passada", theme, width);
        });
    }
}
