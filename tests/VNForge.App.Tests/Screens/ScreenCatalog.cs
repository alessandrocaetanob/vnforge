using Avalonia.Styling;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Demo;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.App.Tests.Screens;

/// <summary>
/// Uma tela do app: o nome do arquivo da captura e o jeito de abri-la. <see cref="Open"/> recebe a pasta do projeto de demonstração e o tema e devolve a
/// janela pronta (assentada, com os dados carregados); quem chama descarta o <see cref="ScreenHost"/>.
/// </summary>
internal sealed record ScreenDef(string Name, Func<string, ThemeVariant, Task<ScreenHost>> Open);

/// <summary>
/// As telas do app, em um lugar só: o <c>ScreenSmokeTests</c> abre cada uma e confere que não lança nem corta texto, e a ferramenta
/// <c>tools/VNForge.Capturas</c> grava o PNG de cada uma, nos dois temas, para o designer. Arquivo sem xUnit.
/// </summary>
internal static class ScreenCatalog
{
    /// <summary>As páginas do trilho e do cabeçalho do jogo que abrem sobre o projeto de demonstração. Uma janela só passa por todas, como a pessoa faz.</summary>
    public static IReadOnlyList<(string Name, NavKey Key)> Pages { get; } =
    [
        ("painel", NavKey.Dashboard),
        ("editor", NavKey.Editor),
        ("conferir", NavKey.Conference),
        ("build", NavKey.Build),
        ("ajustes", NavKey.Settings),
        ("glossario", NavKey.Glossary),
        ("imagens", NavKey.Images),
        ("lancar", NavKey.Release),
        ("biblia", NavKey.Bible),
        ("ficha", NavKey.Sheet),
        ("jogar", NavKey.Play),
    ];

    /// <summary>As telas que não são só uma página do projeto: a Biblioteca, os Personagens, um modo ou painel aberto sobre o Editor, um diálogo ou o assistente.</summary>
    public static IReadOnlyList<ScreenDef> Standalone { get; } =
    [
        new("biblioteca", Library()),
        new("biblioteca-gaveta", Library(l => l.Select(Card(l, "Amaoto no Hakoniwa")), played: true)),
        new("biblioteca-adulto", Library(l =>
        {
            l.IsAdultPopoverOpen = true;
            l.Select(Card(l, "Kuroneko Archive"));
        })),
        new("biblioteca-casar", Library(l =>
        {
            l.Select(Card(l, "Kōrin no Mori"));
            l.Match.Open(l.Selected!.Game);
        })),
        new("biblioteca-exportar", Library(l =>
        {
            l.Export.Folder = "D:\\Catalogo\\minha-estante";
            l.Export.Open();
        })),
        new("biblioteca-varredura", Library(FirstScan)),
        new("biblioteca-sem-portugues", Library(l =>
        {
            l.SelectFilter(LibraryFilter.NoPortuguese);
            l.SelectedSort = l.Sorts[0];
            l.SelectedLayout = l.Layouts[1];
            l.Select(Card(l, "Nanairo Ressha"));
        }, games: PortugueseChecked())),
        new("biblioteca-tags", Library(l =>
        {
            l.Tags.ToggleOptionCommand.Execute(l.Tags.OwnOptions.Single(o => o.Text == "favorito"));
            l.Tags.ToggleOptionCommand.Execute(l.Tags.VndbOptions.Single(o => o.Text == "Escola"));
            l.Tags.IsOpen = true;
        })),
        new("biblioteca-gerenciar-tags", Library(l =>
        {
            l.Tags.OpenManageCommand.Execute(null);
            var rename = l.Tags.Rows.Single(r => r.Name == "rejogar");
            l.Tags.StartRenameCommand.Execute(rename);
            rename.EditText = "rejogar mais tarde";
            l.Tags.AskDeleteCommand.Execute(l.Tags.Rows.Single(r => r.Name == "pra traduzir"));
        })),
        new("biblioteca-vazia", Library(empty: true)),
        new("personagens", Characters(manyTraits: true)),
        new("personagem", Characters("Hana Tsukino", manyTraits: true)),
        new("personagem-mika", Characters("Mika Aoba")),
        new("editor-busca", OpenEditorSearch),
        new("revisao", OpenReview),
        new("leitura", OpenReading),
        new("paleta", OpenPalette),
        new("atalhos", OpenShortcutMap),
        new("traduzir", OpenTranslate),
        new("novo-projeto", Wizard(0)),
        new("novo-projeto-vndb", Wizard(1)),
        new("novo-projeto-ficha", Wizard(2)),
        new("novo-projeto-acentos", Wizard(3)),
        new("novo-projeto-ia", Wizard(4)),
    ];

    /// <summary>
    /// O projeto de demonstração gravado numa pasta de <paramref name="temp"/>, com a caixa de 40 colunas e 3 linhas e a voz dos três personagens do trecho,
    /// como os artboards mostram.
    /// </summary>
    public static string CreateDemoFolder(TempFolder temp)
    {
        var root = DemoProject.Create(temp.SubFolder("projeto"));
        var folder = ProjectFolder.Open(root);
        File.WriteAllText(folder.FormattingPath, """{ "wrap": { "enabled": true, "maxWidth": 40, "maxLines": 3, "charWidth": "uniform" } }""");
        var tones = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["美咲"] = "fala informal e animada; chama o Kenta pelo nome, sem honorífico",
            ["健太"] = "seco e resmungão; frases curtas",
            ["先生"] = "formal; chama os alunos pelo sobrenome",
        };
        folder.SaveCharacters([.. folder.LoadCharacters().Select(c => tones.TryGetValue(c.Source, out var tone) ? c with { Tone = tone } : c)]);
        return root;
    }

    /// <summary>Roda o passo de preparação e, se ele falhar, fecha a janela antes de a exceção subir.</summary>
    private static async Task Prepare(ScreenHost host, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    /// <summary>A Biblioteca com os 12 jogos do quadro. <paramref name="montar"/> põe a tela no estado pedido antes de assentar.</summary>
    private static Func<string, ThemeVariant, Task<ScreenHost>> Library(Action<LibraryViewModel>? montar = null, bool empty = false, bool played = false, IEnumerable<LibraryGame>? games = null, AdultMode adultMode = AdultMode.Blur) =>
        async (_, theme) =>
        {
            var config = new TempFolder();
            var previous = VndbImages.Source;
            VndbImages.Source = new LibrarySamples.Images(new LibraryPrivacy(Adult: adultMode));
            var file = LibrarySamples.Save(config.Path, empty ? new GameLibrary() : LibrarySamples.Library(games));
            var playLog = new PlayLogFile(config.Path);
            if (played)
            {
                PlaySamples(playLog);
            }

            var library = new LibraryViewModel(file, new FakeFolderPicker(null), action => action(), clock: new FixedClock(LibrarySamples.Now), readTranslation: LibrarySamples.Translation, userTags: Tags, sheets: new LibrarySamples.Sheets(), player: played ? new GamePlayer(new NoLauncher(), playLog) : null, playLog: playLog);
            var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(null), new FixedClock(LibrarySamples.Now));
            var host = ScreenHost.Show(new MainWindowViewModel(start, library: library), theme, config, new RestoreImages(previous));
            await Prepare(host, async () =>
            {
                await library.LoadAsync();
                library.ChooseAdultMode(adultMode);
                montar?.Invoke(library);
                host.Settle();
            });
            return host;
        };

    private static LibraryCardViewModel Card(LibraryViewModel library, string title) => library.Cards.Single(c => c.Title == title);

    private static IEnumerable<LibraryGame> PortugueseChecked()
    {
        var checkedAt = LibrarySamples.Now.AddDays(-1);
        return LibrarySamples.Games().Select(g => g.ProjectPath is null && g.IsConfirmed
            ? g with { Portuguese = new LibraryPortuguese(g.VndbId!, g.Title == "Akatsuki no Kōro" ? PortugueseStatus.Partial : PortugueseStatus.None, checkedAt, []) }
            : g);
    }

    /// <summary>A primeira varredura: a pasta, o andamento e a tabela com um jogo em cada estado de suporte e de casamento.</summary>
    private static void FirstScan(LibraryViewModel library)
    {
        library.Mode = LibraryMode.FirstScan;
        var scan = library.Scan;
        scan.Begin("D:\\Jogos\\VN");
        var games = LibrarySamples.Games().Where(g => g.Title != "Fuyu no Shiori").ToList();
        string[] tail = ["Kōrin no Mori", "Amaoto no Hakoniwa", "Shirogane Station 9", "Yūgure Kissaten", "Tōge no Kaji", "Hoshikuzu Diary", "Nanairo Ressha"];
        var order = games.Where(g => !tail.Contains(g.Title)).Concat(tail.Select(t => games.Single(g => g.Title == t))).ToList();
        order[^1] = order[^1] with { VndbId = null, Info = null, Confirmation = LibraryConfirmation.None, SearchedAt = null };
        for (var i = 0; i < order.Count; i++)
        {
            scan.OnFolder(new LibraryScanProgress(i + 1, i + 1, order[i].Folder) { Found = order[i], EstimatedDirectories = 380 });
        }

        scan.OnFolder(new LibraryScanProgress(214, order.Count, "D:\\Jogos\\VN\\nanairo_ressha") { EstimatedDirectories = 380 });
        scan.Rows[^1].Vndb = LibraryScanVndb.Searching;
    }

    /// <summary>Três jogos jogados, como no quadro: hoje (27 h 40 min), há 5 dias (7 h 05 min) e há 12 dias (3 h 20 min).</summary>
    private static void PlaySamples(PlayLogFile playLog)
    {
        foreach (var (title, daysAgo, time) in new[] { ("Amaoto no Hakoniwa", 0, new TimeSpan(27, 40, 0)), ("Hoshikuzu Diary", 5, new TimeSpan(7, 5, 0)), ("Namiuchigiwa no Shiori", 12, new TimeSpan(3, 20, 0)) })
        {
            var end = LibrarySamples.Now.AddDays(-daysAgo).AddMinutes(-20);
            playLog.Record(LibrarySamples.Games().Single(g => g.Title == title).Id, new PlaySession(PlaySession.NewId(), end - time, end, time));
        }
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

    /// <summary>O Jogar só precisa estar habilitado para a captura; nenhuma tela daqui abre jogo.</summary>
    private sealed class NoLauncher : IGameLauncher
    {
        public IGameProcess Start(GameLaunch launch) => throw new NotSupportedException();
    }

    /// <summary>Os Personagens com as fichas inventadas de <see cref="CharactersSamples"/>; <paramref name="open"/> abre a página de um personagem.</summary>
    private static Func<string, ThemeVariant, Task<ScreenHost>> Characters(string? open = null, bool manyTraits = false) => async (_, theme) =>
    {
        var config = new TempFolder();
        await CharactersSamples.SeedAsync(config.Path, matched: true, showSpoilers: true, manyTraits: manyTraits);
        var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(config.Path), new FixedClock(CharactersSamples.Now));
        var vm = new MainWindowViewModel(start);
        vm.Characters.ConfigDirectory = config.Path;
        var host = ScreenHost.Show(vm, theme, config);
        await Prepare(host, async () =>
        {
            await host.GoToAsync(NavKey.Characters);
            if (open is not null)
            {
                vm.Characters.OpenCharacterCommand.Execute(vm.Characters.Items.Single(i => i.Name == open));
                host.Settle();
            }
        });
        return host;
    };

    private static async Task<ScreenHost> OpenEditorSearch(string root, ThemeVariant theme)
    {
        var host = await ScreenHost.OpenProjectAsync(root, theme);
        await Prepare(host, async () =>
        {
            await host.GoToAsync(NavKey.Editor);
            host.Vm.Editor.OpenReplaceCommand.Execute(null);
            host.Vm.Editor.Search.Pattern = "guarda-chuva";
            host.Vm.Editor.Search.Replacement = "sombrinha";
            await host.Vm.Editor.Search.PreviewCommand.ExecuteAsync(null);
            host.Settle();
        });
        return host;
    }

    private static async Task<ScreenHost> OpenReview(string root, ThemeVariant theme)
    {
        var host = await ScreenHost.OpenProjectAsync(root, theme);
        await Prepare(host, async () =>
        {
            await host.GoToAsync(NavKey.Editor);
            host.Vm.Editor.StartReviewCommand.Execute(null);
            host.Settle();
        });
        return host;
    }

    private static async Task<ScreenHost> OpenReading(string root, ThemeVariant theme)
    {
        var host = await ScreenHost.OpenProjectAsync(root, theme);
        await Prepare(host, async () =>
        {
            await host.GoToAsync(NavKey.Editor);
            host.Vm.Editor.StartReadingCommand.Execute(null);
            host.Settle();
        });
        return host;
    }

    private static async Task<ScreenHost> OpenPalette(string root, ThemeVariant theme)
    {
        var host = await ScreenHost.OpenProjectAsync(root, theme);
        await Prepare(host, async () =>
        {
            await host.GoToAsync(NavKey.Editor);
            host.Vm.Palette.Open();
            host.Vm.Palette.Query = "ed";
            host.Settle();
        });
        return host;
    }

    private static async Task<ScreenHost> OpenShortcutMap(string root, ThemeVariant theme)
    {
        var host = await ScreenHost.OpenProjectAsync(root, theme);
        await Prepare(host, async () =>
        {
            await host.GoToAsync(NavKey.Editor);
            host.Vm.ToggleShortcutMapCommand.Execute(null);
            host.Settle();
        });
        return host;
    }

    private static async Task<ScreenHost> OpenTranslate(string root, ThemeVariant theme)
    {
        var vault = new MemoryApiKeyStore();
        vault.Save("sk-ant-chave-da-demonstracao");
        var config = new TempFolder();
        var host = await ScreenHost.OpenProjectAsync(
            root,
            theme,
            start => new MainWindowViewModel(
                start,
                new ResolvedApiKey("sk-ant-chave-da-demonstracao", ApiKeySource.Vault),
                settings: new SettingsViewModel(vault, new AppSettingsFile(config.SubFolder("ajustes")), _ => null, _ => throw new InvalidOperationException("a captura não chama a API")))
            {
                Editor = new EditorViewModel(scriptLabel: DemoProject.Label),
            });
        await Prepare(host, async () =>
        {
            await host.GoToAsync(NavKey.Editor);
            host.Vm.Settings.ExchangeRateText = "5,40";
            host.Vm.Settings.SpendLimitText = "300";
            host.Vm.Settings.WarnAboveText = "250";
            host.Vm.Editor.TranslatePendingCommand.Execute(null);
            await host.Vm.TranslateOpening;
            host.Settle();
        });
        return host;
    }

    /// <summary>O assistente de novo projeto, avançado <paramref name="steps"/> passos: jogo, escolha no VNDB, ficha do VNDB, acentos e IA e gasto.</summary>
    private static Func<string, ThemeVariant, Task<ScreenHost>> Wizard(int steps) => async (_, theme) =>
    {
        var config = new TempFolder();
        var game = config.SubFolder("jogo");
        File.WriteAllText(Path.Combine(game, "Game.exe"), "MZ");
        var wizard = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new FakeEngine()], appSettings: new AppSettingsFile(config.SubFolder("ajustes")), clock: new FixedClock(ScreenHost.Now), vndb: WizardCatalog());
        var start = new StartViewModel(new RecentProjectsFile(config.SubFolder("recentes")), new FakeFolderPicker(null), new FixedClock(ScreenHost.Now));
        var host = ScreenHost.Show(new MainWindowViewModel(start, newProjectWizard: () => wizard), theme, config);
        await Prepare(host, async () =>
        {
            start.NewProjectCommand.Execute(null);
            await wizard.SetGameFolderAsync(game);
            wizard.SetLibraryGame(new LibraryGameContext("Amaoto no Hakoniwa", "v90001", null, "雨音の箱庭"));
            host.Settle();
            if (steps > 0)
            {
                wizard.SetProjectDirectory(Path.Combine(config.Path, "minha-vn"));
                await wizard.NextCommand.ExecuteAsync(null);
                wizard.SearchTitle = "Amaoto";
                await wizard.SearchVndbCommand.ExecuteAsync(null);
            }

            if (steps > 1)
            {
                await wizard.NextCommand.ExecuteAsync(null);
            }

            if (steps > 2)
            {
                await wizard.NextCommand.ExecuteAsync(null);
                wizard.ProjectName = "Minha VN";
                await wizard.NextCommand.ExecuteAsync(null);
            }

            if (steps > 3)
            {
                await wizard.NextCommand.ExecuteAsync(null);
                wizard.RateText = "5,40";
                wizard.LimitText = "300";
            }

            host.Settle();
        });
        return host;
    };

    private static VNForge.Vndb.FakeVndbCatalog WizardCatalog()
    {
        var main = new VndbVisualNovel(
            "v31201", "Amaoto no Hakoniwa", "雨音の箱庭", [], ["Amaoto jogo"], "ja", 0, "2019-05-31", ["ja"], ["win"], null, 3, 1080, 10,
            "Numa cidade onde chove quase o ano inteiro, Kenta volta à escola em que o irmão mais velho estudou e encontra uma caixa de música que só toca quando alguém mente.", []);
        var disc = main with { Id = "v31202", Title = "Amaoto no Hakoniwa: Fan Disc", AltTitle = "雨音の箱庭 ファンディスク", Aliases = [], Released = "2020-03-01", Length = 2, LengthMinutes = 180 };
        VndbCharacter Character(string id, string name, string original, string? gender, string role, params string[] aliases) =>
            new(id, name, original, aliases, null, null, gender is null ? null : new VndbGender(gender, gender), [new VndbCharacterAppearance("v31201", role, 0, null)], []);
        return new VNForge.Vndb.FakeVndbCatalog(
            [main, disc],
            [
                Character("c1", "Kenta Aizawa", "相沢 健太", "m", "main", "Ken", "Kenta-kun"),
                Character("c2", "Aoi Sakura", "佐倉 葵", "f", "primary", "Aoi-chan"),
                Character("c3", "Ren Hayase", "早瀬 蓮", "m", "side", "Ren"),
                Character("c4", "Misaki Todo", "藤堂 美咲", "f", "side", "Todo-sensei"),
                Character("c5", "Coadjuvante", "名無し", null, "appears"),
            ]);
    }

    /// <summary>Devolve a fonte de imagens do VNDB quando a janela da Biblioteca fecha.</summary>
    private sealed class RestoreImages(IVndbImageSource? previous) : IDisposable
    {
        public void Dispose() => VndbImages.Source = previous;
    }
}
