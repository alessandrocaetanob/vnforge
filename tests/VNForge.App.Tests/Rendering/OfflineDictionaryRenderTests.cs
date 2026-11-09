using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Glossary;
using VNForge.Core.Memory;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O dicionário offline do inspetor (#538) nos dois temas, a 1440×1000 e a 1366×768: o cartão da palavra sob o mouse, com o inspetor já no layout compacto da memória de tradução
/// (#537). O cartão não pode cortar texto nem sair da janela, e abrir não pode mexer nos blocos do inspetor. Os PNG seguem <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class OfflineDictionaryRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private const string Frase = "「相沢、佐倉。二人そろって五分遅刻だ」";
    private const string Conjugada = "「平気平気！ほら、行こう！」";

    public static TheoryData<string, int, int> Cases => new()
    {
        { "Dark", 1440, 1000 },
        { "Light", 1440, 1000 },
        { "Dark", 1366, 768 },
        { "Light", 1366, 768 },
    };

    private static string CreateProject(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Amaoto no Hakoniwa", TranslationStatus.Translated);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001",
        [
            new("yst00001:000000", TextUnitKind.Dialogue, "美咲", Frase, "Aizawa, Sakura. Os dois, cinco minutos atrasados.", TranslationStatus.Machine),
            new("yst00001:000001", TextUnitKind.Dialogue, "美咲", Conjugada, "Tranquilo! Vamos, anda!", TranslationStatus.Machine),
        ]);
        folder.SaveGlossary([new GlossaryTerm("遅刻", "atraso")]);
        return root;
    }

    private static TranslationMemory Memory(TempFolder config)
    {
        var memory = new TranslationMemory(config.Path);
        var exact = new TextUnit("a:000001", TextUnitKind.Dialogue, null, Frase, "Aizawa, Sakura. Vocês dois, cinco minutos atrasados.", TranslationStatus.Approved);
        memory.Record(TranslationMemory.KeyOf(Path.Combine(config.Path, "estrela")), "Hoshizora no Kakera", exact, Now.AddDays(-56));
        return memory;
    }

    private async Task<T> InWindow<T>(string themeName, int width, int height, string line, bool installed, Func<Window, EditorViewModel, OfflineDictionary, Task<T>> inspect, Func<HttpClient>? http = null)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp);
        return await fixture.Session.Dispatch(
            async () =>
            {
                using var config = new TempFolder();
                var folder = Path.Combine(config.Path, "dictionary");
                var dictionary = installed ? await SyntheticDictionary.InstalledAsync(folder, http, ToUi) : SyntheticDictionary.NotInstalled(folder, http, postToUi: ToUi);
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var editor = new EditorViewModel(clock: new FixedClock(Now)) { Memory = Memory(config), Dictionary = dictionary };
                var vm = new MainWindowViewModel(start) { Editor = editor };
                var window = new MainWindow { Width = width, Height = height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await editor.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                    await editor.GoToLineAsync("yst00001", line);
                    await editor.Detail!.MemoryLoading;
                    ScreenRenderer.Settle(window);
                    return await inspect(window, editor, dictionary);
                }
                finally
                {
                    window.Close();
                }
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Como o app faz: o aviso de mudança do dicionário, que nasce numa thread do pool, chega à interface pela fila da thread dela.</summary>
    private static void ToUi(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);

    private static LineDetailView Inspector(Window window) => window.GetVisualDescendants().OfType<LineDetailView>().First(v => v.IsEffectivelyVisible);

    private static void AssertCardFits(Window window, LineDetailView inspector)
    {
        var host = inspector.FindControl<WordCardView>("WordCardHost")!;
        Assert.True(inspector.FindControl<Popup>("WordPopup")!.IsOpen);
        Assert.True(host.IsEffectivelyVisible);
        Assert.True(host.Bounds.Height > 100, $"O cartão tem {host.Bounds.Height} px de altura.");
        var origin = host.TranslatePoint(default, window) ?? default;
        Assert.True(origin.X >= 0 && origin.Y >= 0 && origin.X + host.Bounds.Width <= window.Bounds.Width && origin.Y + host.Bounds.Height <= window.Bounds.Height,
            $"O cartão ({origin.X:0},{origin.Y:0} {host.Bounds.Width:0}x{host.Bounds.Height:0}) sai da janela de {window.Bounds.Width:0}x{window.Bounds.Height:0}.");
        Assert.Empty(ClippedTextHeadlessTests.FindClipped(host));

        // A largura fica entre 360 e 372 px (os quadros), ou até a do original quando ele é mais estreito, e o cartão fica dentro da coluna do inspetor: não cobre a lista de linhas.
        var box = inspector.FindControl<Border>("OriginalBox")!;
        Assert.InRange(host.Bounds.Width, Math.Min(WordCardWidth.Min, box.Bounds.Width) - 0.5, Math.Min(WordCardWidth.Max, box.Bounds.Width) + 0.5);

        var inspectorLeft = (inspector.TranslatePoint(default, window) ?? default).X;
        Assert.True(origin.X >= inspectorLeft - 1, $"O cartão começa em x={origin.X:0}, antes do inspetor (x={inspectorLeft:0}): cobriria a lista de linhas.");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Palavra_simples_com_o_termo_ja_no_glossario(string theme, int width, int height)
    {
        var saved = await InWindow(theme, width, height, "yst00001:000000", installed: true, (window, editor, _) =>
        {
            var inspector = Inspector(window);
            var before = inspector.FindControl<Border>("MemoryFragment")!.Bounds;
            var sceneBefore = inspector.FindControl<Control>("TranslationFragment")!.Bounds;

            editor.Detail!.HoverSource(Frase.IndexOf("遅刻", StringComparison.Ordinal) + 1);
            ScreenRenderer.Settle(window);

            AssertCardFits(window, inspector);
            Assert.True(editor.Detail.InGlossary);
            Assert.Equal(before, inspector.FindControl<Border>("MemoryFragment")!.Bounds);
            Assert.Equal(sceneBefore, inspector.FindControl<Control>("TranslationFragment")!.Bounds);
            Assert.True(editor.Detail.HasMemory);
            var highlight = inspector.FindControl<WordHighlight>("SourceHighlight")!;
            Assert.Single(highlight.Rects());
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName($"editor-dicionario-{width}", theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark)));
            return Task.FromResult(true);
        });

        Assert.True(saved);
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task Palavra_conjugada_com_varias_leituras_e_o_resto_da_entrada(string theme)
    {
        var variant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await InWindow(theme, ScreenRenderer.Width, ScreenRenderer.Height, "yst00001:000001", installed: true, (window, editor, _) =>
        {
            var inspector = Inspector(window);
            editor.Detail!.HoverSource(Conjugada.IndexOf("行こう", StringComparison.Ordinal) + 1);
            ScreenRenderer.Settle(window);

            AssertCardFits(window, inspector);
            Assert.Equal("行く", editor.Detail.WordText);
            Assert.True(editor.Detail.CanAddWordToGlossary);
            Assert.Contains(inspector.FindControl<WordCardView>("WordCardHost")!.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Content as string == "Adicionar ao glossário");
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("editor-dicionario-conjugada", variant)));

            editor.Detail.ExpandWordCommand.Execute(null);
            ScreenRenderer.Settle(window);
            AssertCardFits(window, inspector);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("editor-dicionario-aberta", variant)));
            return Task.FromResult(true);
        });
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Sem_o_dicionario_instalado_o_cartao_oferece_o_download(string theme, int width, int height)
    {
        var variant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await InWindow(theme, width, height, "yst00001:000000", installed: false, (window, editor, _) =>
        {
            var inspector = Inspector(window);
            editor.Detail!.HoverSource(Frase.IndexOf("遅刻", StringComparison.Ordinal));
            ScreenRenderer.Settle(window);

            AssertCardFits(window, inspector);
            Assert.True(editor.Detail.IsOfferCard);
            var buttons = inspector.FindControl<WordCardView>("WordCardHost")!.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content).ToList();
            Assert.Contains("Baixar o dicionário", buttons);
            Assert.Contains("Agora não", buttons);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName($"editor-dicionario-baixar-{width}", variant)));
            return Task.FromResult(true);
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task Durante_o_download_o_cartao_mostra_o_andamento_e_o_botao_Cancelar(string theme)
    {
        var variant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await InWindow(theme, ScreenRenderer.Width, ScreenRenderer.Height, "yst00001:000000", installed: false, async (window, editor, dictionary) =>
        {
            var inspector = Inspector(window);
            editor.Detail!.HoverSource(Frase.IndexOf("遅刻", StringComparison.Ordinal));
            var baixando = editor.Detail.InstallDictionaryCommand.ExecuteAsync(null);
            ScreenRenderer.Settle(window);

            AssertCardFits(window, inspector);
            Assert.True(editor.Detail.IsInstallingCard);
            var buttons = inspector.FindControl<WordCardView>("WordCardHost")!.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content).ToList();
            Assert.Contains("Cancelar", buttons);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("editor-dicionario-baixando", variant)));

            editor.Detail.CancelDictionaryInstallCommand.Execute(null);
            await baixando;
            ScreenRenderer.Settle(window);
            Assert.False(editor.Detail.ShowWordCard);
            return true;
        }, () => new HttpClient(new SyntheticDictionary.HangingServer()));
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task Falha_do_download_mostra_o_erro_no_cartao(string theme)
    {
        var variant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await InWindow(theme, ScreenRenderer.Width, ScreenRenderer.Height, "yst00001:000000", installed: false, async (window, editor, _) =>
        {
            var inspector = Inspector(window);
            editor.Detail!.HoverSource(Frase.IndexOf("遅刻", StringComparison.Ordinal));
            await editor.Detail.InstallDictionaryCommand.ExecuteAsync(null);
            ScreenRenderer.Settle(window);
            ScreenRenderer.Settle(window);

            AssertCardFits(window, inspector);
            Assert.True(editor.Detail.IsFailedCard);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("editor-dicionario-erro", variant)));
            return true;
        }, () => new HttpClient(new SyntheticDictionary.TimeoutServer()));
    }

    [Fact]
    public async Task O_mouse_de_verdade_sobre_a_palavra_abre_o_cartao()
    {
        await InWindow("Light", ScreenRenderer.Width, ScreenRenderer.Height, "yst00001:000000", installed: true, (window, editor, _) =>
        {
            var inspector = Inspector(window);
            var text = inspector.FindControl<TextBlock>("SourceText")!;
            var index = Frase.IndexOf("遅刻", StringComparison.Ordinal);
            var rect = text.TextLayout.HitTestTextPosition(index);
            var point = text.TranslatePoint(rect.Center, window) ?? default;

            window.MouseMove(point);
            ScreenRenderer.Settle(window);

            Assert.True(editor.Detail!.IsEntryCard);
            Assert.Equal("遅刻", editor.Detail.WordText);
            return Task.FromResult(true);
        });
    }
}
