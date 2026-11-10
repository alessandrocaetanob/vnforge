using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Demo;
using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// A aba Ficha do jogo (#554 e #545) montada de verdade nos dois temas a 1440×1000, dentro do shell, com o projeto de demonstração ligado ao VNDB e a ficha inventada de
/// <see cref="GameSheetSamples"/>: a ficha com sinopse, tags, personagens, dados e spoiler; a revisão bloco a bloco com um traço de spoiler já mostrado; e os cartões de personagem
/// nos quatro estados (com imagem, sem imagem, escondido por spoiler e com descrição longa). Os PNG seguem <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class GameSheetRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant ThemeOf(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private async Task Abrir(ThemeVariant theme, Func<MainWindow, MainWindowViewModel, Task> verificar, bool translate = true)
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                await GameSheetSamples.SeedAsync(config.Path, translate);
                var held = new LibraryGame("g1", "C:\\Jogos\\Amaoto", null, LibraryEngine.Unknown, "Amaoto no Hakoniwa", null, GameSheetSamples.Now) { VndbId = GameSheetSamples.Id };
                new LibraryFile(config.Path).Save(new GameLibrary([held]));
                new UserTagsFile(config.Path).Update(tags => tags.Add("g1", "favorito"));
                ProjectFolder.Open(demoFolder.Root).SaveVndbLink(new VndbLink(GameSheetSamples.Id, "Amaoto no Hakoniwa", "雨音の箱庭", null, []));
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demoFolder.Root), new FixedClock(GameSheetSamples.Now));
                var vm = new MainWindowViewModel(start) { Editor = new EditorViewModel(scriptLabel: DemoProject.Label) };
                vm.Sheet.ConfigDirectory = config.Path;
                vm.Sheet.CatalogFactory = GameSheetSamples.Catalog;
                vm.Sheet.TranslatorFactory = GameSheetSamples.Translator;
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await vm.Editor.Loading;
                    await vm.Sheet.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Sheet);
                    await vm.Dashboard.ReloadAsync();
                    ScreenRenderer.Settle(window);

                    await verificar(window, vm);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static GameSheetView Page(Window window) => Assert.Single(window.GetVisualDescendants().OfType<GameSheetView>(), v => v.IsEffectivelyVisible);

    private static List<string?> Texts(Visual root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text)];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_ficha_tem_sinopse_tags_personagens_dados_estado_do_texto_e_spoiler_como_no_quadro(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, (window, vm) =>
        {
            var page = Page(window);
            Assert.Equal(GameTab.Sheet, vm.SelectedGameTab?.Tab);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "PlayButton" && b.IsEffectivelyVisible);
            var texts = Texts(page);

            // Esquerda: a ordem dos três cartões e o texto de cada um.
            string[] order = ["Sinopse", "Revisada", "Ver o original", "Gêneros e tags", "Traduzidas pela IA, valem para todos os jogos", "Drama escolar", "Mistério", "Slice of life", "Chuva", "Clube de leitura", "Caixa de música", "favorito", "2 tags com spoiler", "Personagens", "· 7 no VNDB", "Ver todos", "Kenta Aizawa"];
            AssertInOrder(texts, order);
            Assert.Contains("Numa cidade onde chove quase o ano inteiro", Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "SynopsisText").Text, StringComparison.Ordinal);
            Assert.Contains("O gênero vem em dois valores: o do começo do jogo e o real. O real fica escondido até você pedir.", texts);

            // Direita: dados, estado do texto e spoiler.
            AssertInOrder(texts, ["Sobre o jogo", "Duração", "Média · 18 h (47 votos)", "Lançado", "2019, em japonês", "Nota no VNDB", "7,9 de 10", "VNDB", GameSheetSamples.Id, "Texto em português", "1 aprovado", "2 revisados", "1 traduzido", "4 pela IA", "Revisar a ficha", "Traduzir de novo com a IA", "Spoiler", "Escondido", "Leve", "Tudo", "Traços, tags e papéis com spoiler ficam escondidos. O nível vem do VNDB."]);

            // Os seis primeiros cartões, com papel, etapa e traços, e o botão "Ver todos" para o sétimo.
            var tiles = page.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("sheet-tile")).ToList();
            Assert.Equal(6, tiles.Count);
            Assert.Equal(["Kenta Aizawa", "Aoi Sakura", "Ren Hayase", "Haruto Mine", "Misaki Tōdō", "Tsubaki Inoue"], tiles.Select(t => Texts(t)[0]));
            AssertInOrder(Texts(tiles[0]), ["Kenta Aizawa", "相沢 健太", "Protagonista", "Aprovado", "Cabelo curto, Esportista"]);
            AssertInOrder(Texts(tiles[1]), ["Aoi Sakura", "佐倉 葵", "Principal", "Revisado", "Cabelo longo, Quieta, Estudante, Rata de biblioteca"]);
            Assert.Equal(6, page.GetVisualDescendants().OfType<Portrait>().Count());

            Assert.Equal(["Escondido", "Leve", "Tudo"], page.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "SpoilerLevels").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("ficha-v6", theme)));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_revisao_mostra_o_original_ao_lado_do_portugues_os_botoes_o_progresso_e_o_traco_com_spoiler(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, async (window, vm) =>
        {
            // O quadro: a descrição de Aoi acabou de ser traduzida pela IA e os traços dela já foram revisados; o filtro é "Só IA" e o spoiler está no nível 2.
            var store = new SheetStore(vm.Sheet.ConfigDirectory!);
            await new SheetTranslationRunner(GameSheetSamples.Translator(), store).RetranslateAsync(GameSheetSamples.Id, [SheetField.OfCharacter("c2")]);
            await vm.Sheet.ReloadCommand.ExecuteAsync(null);
            vm.Sheet.SpoilerLevel = 2;
            vm.Sheet.StartReviewCommand.Execute(null);
            var review = vm.Sheet.Review;
            review.SelectedFilter = review.Filters[1];
            while (review.Current!.Title != "Aoi Sakura")
            {
                review.NextCommand.Execute(null);
            }

            ScreenRenderer.Settle(window);
            var page = Page(window);
            var texts = Texts(page);
            Assert.Equal("Biblioteca › " + vm.ProjectTitle + " › Revisar a ficha", vm.Breadcrumb);
            Assert.False(vm.ShowPlayButton);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "PlayButton" && b.IsEffectivelyVisible);
            AssertInOrder(texts, ["Revisar a ficha", "Aoi Sakura · bloco 2 de 6 · revisado igual a uma fala", "Tudo", "Só IA", "Só pendentes", "Descrição de Aoi Sakura", "Traduzido pela IA", "VNDB, em inglês", "Português", "Traduzir de novo", "Marcar revisado", "Aprovar e próxima", "Traços de Aoi Sakura", "Revisado", "Traços com spoiler", "Mostrando nível 2", "Cabelo longo", "Quieta", "Estudante", "Rata de biblioteca", "Irmã de Kenta · spoiler 2", "Mente · spoiler 1"]);
            AssertInOrder(texts, ["Progresso da ficha", "2 de 9", "blocos", "1 aprovado", "1 revisado", "1 traduzido", "6 pela IA", "O que fica no jogo"]);
            Assert.Contains("Kenta's classmate. She rarely speaks, reads on the roof on rainy days and keeps the key to the garden.", texts);
            Assert.Equal(["Colega de turma de Kenta. Fala pouco, lê no telhado em dias de chuva e guarda a chave do jardim.", "Cabelo longo, Quieta, Estudante, Rata de biblioteca"], page.GetVisualDescendants().OfType<TextBox>().Select(t => t.Text));

            var field = page.GetVisualDescendants().OfType<TextBox>().First();
            field.Focus();
            ScreenRenderer.Settle(window);
            Assert.True(field.IsFocused);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("ficha-revisar-v6", theme)));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Os_cartoes_de_personagem_tem_os_quatro_estados_do_quadro(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, (window, vm) =>
        {
            // Ver todos: o sétimo cartão está escondido por spoiler de nível 2, com o "Mostrar".
            vm.Sheet.ToggleAllCharactersCommand.Execute(null);
            ScreenRenderer.Settle(window);
            var page = Page(window);
            var tiles = page.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("sheet-tile")).ToList();
            Assert.Equal(7, tiles.Count);
            AssertInOrder(Texts(tiles[^1]), ["Nome escondido", "Spoiler de nível 2", "Mostrar"]);
            Assert.Equal("Ver menos", Texts(page).Single(t => t == "Ver menos"));
            Assert.Single(page.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Classes.Contains("sheet-spoil") && Texts(b).Contains("Mostrar"));
            page.GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("ficha-personagens-v6", theme)));

            // Mostrar sobe o nível e o cartão ganha o nome; o retrato do VNDB ausente vira o ícone do quadro.
            var show = page.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Classes.Contains("sheet-spoil") && Texts(b).Contains("Mostrar"));
            show.Command!.Execute(show.CommandParameter);
            ScreenRenderer.Settle(window);
            Assert.Equal(2, vm.Sheet.SpoilerLevel);
            var ghost = page.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("sheet-tile") && Texts(b).Contains("Ghost"));
            AssertInOrder(Texts(ghost), ["Ghost", "Sem imagem no VNDB", "Secundário"]);
            Assert.Contains(ghost.GetVisualDescendants().OfType<PathIcon>(), i => i.IsEffectivelyVisible);
            page.GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("ficha-personagens-revelado-v6", theme)));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Sem_a_ficha_buscada_a_aba_oferece_buscar_no_VNDB(string themeName)
    {
        var theme = ThemeOf(themeName);
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                ProjectFolder.Open(demoFolder.Root).SaveVndbLink(new VndbLink(GameSheetSamples.Id, "Amaoto no Hakoniwa", "雨音の箱庭", null, []));
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demoFolder.Root), new FixedClock(GameSheetSamples.Now));
                var vm = new MainWindowViewModel(start) { Editor = new EditorViewModel(scriptLabel: DemoProject.Label) };
                vm.Sheet.ConfigDirectory = config.Path;
                vm.Sheet.CatalogFactory = GameSheetSamples.Catalog;
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await vm.Sheet.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Sheet);
                    ScreenRenderer.Settle(window);

                    var page = Page(window);
                    var empty = Assert.Single(page.GetVisualDescendants().OfType<EmptyState>());
                    Assert.Equal(vm.Sheet.EmptyMessage, empty.Message);
                    var fetch = Assert.Single(page.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && Equals(b.Content, "Buscar a ficha no VNDB"));
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("ficha-sem-busca-v6", theme)));

                    await vm.Sheet.FetchCommand.ExecuteAsync(null);
                    ScreenRenderer.Settle(window);
                    Assert.Contains("Sinopse", Texts(page));
                    Assert.True(fetch.IsEnabled || !fetch.IsEffectivelyVisible);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Clicar_na_aba_Ficha_ou_apertar_Esc_na_revisao_volta_para_a_ficha_e_PageDown_passa_de_bloco()
    {
        await Abrir(ThemeVariant.Dark, (window, vm) =>
        {
            vm.Sheet.StartReviewCommand.Execute(null);
            ScreenRenderer.Settle(window);
            var page = Page(window);
            Assert.True(vm.Sheet.IsReviewing);
            Assert.Equal("Sinopse", vm.Sheet.Review.Current!.Title);

            page.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageDown });
            Assert.Equal("Gêneros e tags", vm.Sheet.Review.Current!.Title);
            page.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageUp });
            Assert.Equal("Sinopse", vm.Sheet.Review.Current!.Title);

            page.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(vm.Sheet.IsReviewing);
            Assert.True(vm.ShowPlayButton);

            vm.Sheet.StartReviewCommand.Execute(null);
            vm.SelectedGameTab = vm.GameTabs.Single(t => t.Tab == GameTab.Sheet);
            Assert.False(vm.Sheet.IsReviewing);
            Assert.Equal("Biblioteca › " + vm.ProjectTitle, vm.Breadcrumb);
            return Task.CompletedTask;
        });
    }

    private static void AssertInOrder(IReadOnlyList<string?> texts, string[] expected)
    {
        var from = 0;
        foreach (var text in expected)
        {
            var at = -1;
            for (var i = from; i < texts.Count; i++)
            {
                if (texts[i] == text)
                {
                    at = i;
                    break;
                }
            }

            Assert.True(at >= 0, $"Faltou \"{text}\" depois da posição {from}. Textos: {string.Join(" | ", texts)}");
            from = at + 1;
        }
    }
}
