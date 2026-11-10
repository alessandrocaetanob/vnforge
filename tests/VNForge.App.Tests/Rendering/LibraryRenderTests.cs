using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Library;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// A Biblioteca (#548) e as peças comuns dela (#545) montadas de verdade no shell, nos dois temas, a 1440×1000 como o quadro: a grade com os estados do jogo, a gaveta, o diálogo do
/// casamento, a primeira varredura e a biblioteca vazia. Os PNGs <c>biblioteca-*</c> seguem <c>VNFORGE_RENDER_DIR</c> e vão para <c>docs/design/telas/v6/</c>.
/// </summary>
public sealed class LibraryRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant ThemeOf(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private static List<string> Texts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).SelectMany(t => t.Inlines is { Count: > 0 } inlines ? inlines.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text ?? string.Empty) : [t.Text ?? string.Empty]).Where(t => t.Length > 0)];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Grade_mostra_os_estados_do_jogo_a_busca_os_filtros_e_o_cabecalho(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, _ => { }, (window, vm) =>
        {
            var texts = Texts(window);
            Assert.Contains("Biblioteca", texts);
            Assert.Contains("12 jogos em 2 pastas · varredura há 2 horas", texts);
            Assert.Contains("Varrer de novo", texts);
            Assert.Contains("Adicionar pasta", texts);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBox>(), t => t.PlaceholderText == "Buscar por título, original ou tag");
            Assert.Equal(["Grade", "Lista"], window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("libseg")).GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

            var chips = window.GetVisualDescendants().OfType<FilterChip>().ToList();
            Assert.Equal(["Todos", "Em tradução", "Sem português", "Sem suporte"], chips.Select(c => c.Label));
            Assert.Equal([12, 2, 9, 5], chips.Select(c => c.Count));
            Assert.True(chips[0].IsChecked);

            foreach (var expected in new[] { "Yu-RIS", "Siglus · sem suporte", "KiriKiri · sem suporte", "Ren'Py · sem suporte", "Engine não reconhecida", "Conteúdo adulto escondido", "18+" })
            {
                Assert.True(texts.Contains(expected), $"falta '{expected}' em: {string.Join(" | ", texts)}");
            }

            Assert.Contains("Média · 18 h · 38% traduzida", texts);
            Assert.Contains("Curta · 7 h · 100% traduzida", texts);
            Assert.Contains("Média · 20 h · 12% traduzida", texts);
            Assert.Contains("Longa · 42 h", texts);
            Assert.Contains("sem dado", texts);

            var covers = window.GetVisualDescendants().OfType<Cover>().Where(c => c.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(covers);
            Assert.All(covers, c => Assert.InRange(c.Bounds.Height, (c.Bounds.Width * 4 / 3) - 1, (c.Bounds.Width * 4 / 3) + 1));
            Assert.InRange(covers[0].Bounds.Width, 200, 260);

            var fills = window.GetVisualDescendants().OfType<FractionBar>().Where(b => b.IsEffectivelyVisible && b.Height == 5).Select(b => b.Value).Order().ToList();
            Assert.Equal([0.12, 0.38, 1.0], fills);

            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("biblioteca-grade", theme)));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Gaveta_abre_no_jogo_escolhido_com_a_ficha_as_tags_e_o_andamento(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, library => library.Select(library.Cards.Single(c => c.Title == "Amaoto no Hakoniwa")), (window, vm) =>
        {
            var switchBox = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("libseg"));
            var drawerHost = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DrawerHost");
            Assert.True(switchBox.TranslatePoint(new Point(switchBox.Bounds.Width, 0), window)!.Value.X <= drawerHost.TranslatePoint(default, window)!.Value.X, "Grade e Lista passam do começo da gaveta");
            var texts = Texts(window);
            foreach (var expected in new[]
                     {
                         "Engine", "Yu-RIS · traduz", "VNDB", "v31201 · casado", "Duração", "Média · 18 h", "Lançado", "2019", "Abrir jogo", "Jogar", "Revisada",
                         LibrarySamples.Synopsis, "Escola", "Mistério", "Chuva", "favorito", "+6", "Tradução", "38%", "18.802 de 48.211 linhas prontas",
                     })
            {
                Assert.True(texts.Contains(expected), $"falta '{expected}' em: {string.Join(" | ", texts)}");
            }

            Assert.Contains("雨音の箱庭", texts);
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PlayButton").IsEnabled);
            var drawer = window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("lib-drawer"));
            Assert.Equal(396, drawer.Bounds.Width, 1);
            Assert.Equal(new CornerRadius(20), drawer.CornerRadius);
            var selected = window.GetVisualDescendants().OfType<Border>().Where(b => b.IsEffectivelyVisible && b.BorderThickness == new Thickness(3) && b.CornerRadius == new CornerRadius(22));
            Assert.Single(selected);

            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("biblioteca-gaveta", theme)));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Dialogo_do_casamento_lista_os_candidatos_com_o_que_o_VNDB_sabe_deles(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, library =>
        {
            library.Select(library.Cards.Single(c => c.Title == "Kōrin no Mori"));
            library.Match.Open(library.Selected!.Game);
        }, (window, vm) =>
        {
            var texts = Texts(window);
            Assert.Contains("Qual é este jogo no VNDB?", texts);
            Assert.Contains("O VNForge achou 3 candidatos para a pasta", texts);
            Assert.Contains(". Confirme o certo para trazer capa, sinopse e personagens.", texts);
            foreach (var expected in new[]
                     {
                         "Kōrin no Mori", "光輪の森", "2021 · Curta · 4 h · nota 7,6", "Título da janela", "Pasta", "Exe: game.exe",
                         "Kōrin no Mori: Gaiden", "光輪の森 外伝", "2022 · Curta · 2 h · nota 7,1", "2014 · Curta · 3 h · nota 6,4", "Pasta parecida", "Kōrin", "Só o título",
                         "Não é nenhum deles", "Buscar no VNDB", "Depois", "Confirmar este",
                     })
            {
                Assert.True(texts.Contains(expected), $"falta '{expected}' em: {string.Join(" | ", texts)}");
            }

            var options = window.GetVisualDescendants().OfType<RadioButton>().Where(r => r.Classes.Contains("option")).ToList();
            Assert.Equal(3, options.Count);
            Assert.Equal([true, false, false], options.Select(r => r.IsChecked == true));

            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("biblioteca-casamento", theme)));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Primeira_varredura_mostra_a_pasta_o_andamento_a_tabela_e_o_ate_agora(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, library =>
        {
            library.Mode = LibraryMode.FirstScan;
            var scan = library.Scan;
            scan.Begin("D:\\Jogos\\VN");
            var games = LibrarySamples.Games().Where(g => g.Title != "Fuyu no Shiori").ToList();
            string[] tail = ["Kōrin no Mori", "Amaoto no Hakoniwa", "Shirogane Station 9", "Yūgure Kissaten", "Tōge no Kaji", "Hoshikuzu Diary", "Nanairo Ressha"];
            var order = games.Where(g => !tail.Contains(g.Title)).Concat(tail.Select(t => games.Single(g => g.Title == t))).ToList();
            var last = order[^1] with { VndbId = null, Info = null, Confirmation = LibraryConfirmation.None, SearchedAt = null };
            order[^1] = last;
            for (var i = 0; i < order.Count; i++)
            {
                scan.OnFolder(new LibraryScanProgress(i + 1, i + 1, order[i].Folder) { Found = order[i], EstimatedDirectories = 380 });
            }

            scan.OnFolder(new LibraryScanProgress(214, order.Count, "D:\\Jogos\\VN\\nanairo_ressha") { EstimatedDirectories = 380 });
            scan.Rows[^1].Vndb = LibraryScanVndb.Searching;
        }, (window, vm) =>
        {
            var texts = Texts(window);
            foreach (var expected in new[]
                     {
                         "Primeira varredura", "O VNForge só lê as pastas. Nada é copiado nem alterado nos jogos.", "Pasta dos jogos", "D:\\Jogos\\VN", "Trocar pasta",
                         "Incluir subpastas", "Casar com o VNDB", "Ler a pasta toda de novo ao abrir o app", "Varrendo", "214 de 380 pastas · D:\\Jogos\\VN\\nanairo_ressha", "Pausar",
                         "Pasta", "Engine", "Suporte", "VNDB", "korin_no_mori", "Ren'Py", "Sem suporte ainda", "Confirmar", "Casado", "Traduz", "Sem engine", "Sem casamento",
                         "Não reconhecida", "Procurando no VNDB", "Até agora", "Reconhecidos", "Não reconhecidos", "A confirmar no VNDB", "Depois da varredura",
                         "Você confirma os jogos a confirmar um por vez. Os que o VNDB não acha ficam sem capa, e você pode ligar o jogo a mão.",
                     })
            {
                Assert.True(texts.Contains(expected), $"falta '{expected}' em: {string.Join(" | ", texts)}");
            }

            Assert.Equal(7, window.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "ScanRows").GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("lib-row")));
            Assert.Equal("Varrendo D:\\Jogos\\VN", vm.LibraryCountText);
            Assert.Equal(["6", "4", "1", "1"], window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("lib-h2") && t.FontSize == 22).Select(t => t.Text));

            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("biblioteca-varredura", theme)));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Biblioteca_vazia_convida_a_apontar_a_pasta(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, _ => { }, (window, vm) =>
        {
            var texts = Texts(window);
            foreach (var expected in new[]
                     {
                         "Aponte onde estão os seus jogos", "O VNForge lê a pasta, reconhece a engine de cada jogo e busca capa, sinopse e personagens no VNDB. Você só confirma.",
                         "Escolher pasta", "Abrir um projeto que já existe", "Ou solte uma pasta ou um drive aqui", "O VNForge só lê. Antes de mexer num jogo, ele sempre faz uma cópia de segurança.",
                         "Nenhum jogo ainda",
                     })
            {
                Assert.True(texts.Contains(expected), $"falta '{expected}' em: {string.Join(" | ", texts)}");
            }

            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("biblioteca-vazia", theme)));
        }, empty: true);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Filtro_por_tag_junta_a_sua_e_a_do_VNDB_com_o_popover_das_tags(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, library =>
        {
            library.Tags.ToggleOptionCommand.Execute(library.Tags.OwnOptions.Single(o => o.Text == "favorito"));
            library.Tags.ToggleOptionCommand.Execute(library.Tags.VndbOptions.Single(o => o.Text == "Escola"));
            library.Tags.IsOpen = true;
        }, (window, vm) =>
        {
            var texts = Texts(window);
            foreach (var expected in new[] { "4 de 12 jogos · favorito e Escola", "Filtro salvo", "Tem as duas tags", "Tags", "favorito", "Escola", "+8" })
            {
                Assert.True(texts.Contains(expected), $"falta '{expected}' em: {string.Join(" | ", texts)}");
            }

            Assert.Equal(2, window.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("lib-active")));
            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("biblioteca-tags", theme)));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Dialogo_das_tags_lista_renomeia_e_apaga(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, library =>
        {
            library.Tags.OpenManageCommand.Execute(null);
            library.Tags.StartRenameCommand.Execute(library.Tags.Rows.Single(r => r.Name == "rejogar"));
            library.Tags.Rows.Single(r => r.Name == "rejogar").EditText = "rejogar mais tarde";
            library.Tags.AskDeleteCommand.Execute(library.Tags.Rows.Single(r => r.Name == "pra traduzir"));
        }, (window, vm) =>
        {
            var texts = Texts(window);
            foreach (var expected in new[]
                     {
                         "Suas tags", "Ficam só neste computador e não sobem para o VNDB. Valem para jogos, não para personagens.", "favorito", "5 jogos", "3 jogos", "emprestado", "1 jogo",
                         "Apagar “pra traduzir”?", "A tag sai dos 6 jogos que a usam. As tags do VNDB não mudam.", "Cancelar", "Salvar", "Apagar tag", "Adicionar", "Fechar",
                     })
            {
                Assert.True(texts.Contains(expected), $"falta '{expected}' em: {string.Join(" | ", texts)}");
            }

            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("biblioteca-tags-gerenciar", theme)));
        });
    }

    [Fact]
    public async Task Botao_Tags_abre_e_fecha_o_popover_no_clique_e_o_clique_fora_fecha()
    {
        await Abrir(ThemeVariant.Dark, _ => { }, (window, vm) =>
        {
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TagsButton");
            var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            void Click(Point at)
            {
                window.MouseDown(at, MouseButton.Left);
                window.MouseUp(at, MouseButton.Left);
                ScreenRenderer.Settle(window);
            }

            Click(center);
            Assert.True(vm.Library.Tags.IsOpen);
            Click(center);
            Assert.False(vm.Library.Tags.IsOpen);
            Click(center);
            Assert.True(vm.Library.Tags.IsOpen);
            Click(new Point(700, 800));
            Assert.False(vm.Library.Tags.IsOpen);
        });
    }

    private async Task Abrir(ThemeVariant theme, Action<LibraryViewModel> montar, Action<MainWindow, MainWindowViewModel> verificar, bool empty = false)
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var previous = VndbImages.Source;
                VndbImages.Source = new LibrarySamples.Images();
                var file = LibrarySamples.Save(config.Path, empty ? new GameLibrary() : LibrarySamples.Library());
                var library = new LibraryViewModel(file, new FakeFolderPicker(null), action => action(), clock: new FixedClock(LibrarySamples.Now), readTranslation: LibrarySamples.Translation, userTags: Tags, sheets: new LibrarySamples.Sheets());
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(null), new FixedClock(LibrarySamples.Now));
                var vm = new MainWindowViewModel(start, library: library);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await library.LoadAsync();
                    montar(library);
                    ScreenRenderer.Settle(window);
                    verificar(window, vm);
                }
                finally
                {
                    window.Close();
                    VndbImages.Source = previous;
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static UserTags Tags()
    {
        var ids = LibrarySamples.Games().Select(g => g.Id).ToList();
        var tags = new UserTags();
        foreach (var index in new[] { 0, 3, 5, 8, 10 })
        {
            tags.Add(ids[index], "favorito");
        }

        foreach (var index in new[] { 1, 4, 9 })
        {
            tags.Add(ids[index], "rejogar");
        }

        foreach (var index in new[] { 1, 2, 4, 6, 9, 11 })
        {
            tags.Add(ids[index], "pra traduzir");
        }

        tags.Add(ids[7], "emprestado");
        return tags;
    }
}
