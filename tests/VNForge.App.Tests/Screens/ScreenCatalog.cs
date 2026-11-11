using Avalonia.Styling;
using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Bible;
using VNForge.Core.Demo;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;
using VNForge.Vndb;

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
            l.Tags.ToggleOpenCommand.Execute(null);
            l.Tags.ToggleOpenCommand.Execute(null);
            l.Tags.ToggleOpenCommand.Execute(null);
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
        new("biblioteca-adulto-mostrar", Library(l => l.SetAdultModeCommand.Execute(l.AdultModes.Single(m => m.Mode == AdultMode.Show)), adultMode: AdultMode.Hide)),
        new("biblioteca-adulto-esconder", Library(l =>
        {
            l.IsAdultPopoverOpen = true;
            l.SetAdultModeCommand.Execute(l.AdultModes.Single(m => m.Mode == AdultMode.Hide));
        })),
        new("personagens", Characters(manyTraits: true)),
        new("personagem", Characters("Hana Tsukino", manyTraits: true)),
        new("personagens-busca", Characters(search: "Hana")),
        new("personagem-mika", Characters("Mika Aoba")),
        new("editor-busca", OpenEditorSearch),
        new("jogar-tempo", Play(playing: false)),
        new("jogar-em-curso", Play(playing: true)),
        new("ajustes-estilo", SettingsTab(1)),
        new("ajustes-formatacao", SettingsTab(2)),
        new("ajustes-acentos", SettingsTab(3)),
        new("ajustes-dicionarios", SettingsTab(SettingsViewModel.DictionariesTab)),
        new("ajustes-aparencia", SettingsTab(5)),
        new("ajustes-privacidade", SettingsTab(SettingsViewModel.PrivacyTab)),
        new("ajustes-vndb", SettingsTab(8, vndb: true)),
        new("ficha-vndb", Sheet(review: false)),
        new("ficha-revisao", Sheet(review: true)),
        new("ficha-nao-baixada", Sheet(review: false, seeded: false)),
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
    /// como os artboards mostram. Com <paramref name="trimmed"/> ficam só os oito scripts nomeados (o smoke); sem ele, os 312 do artboard (as capturas).
    /// </summary>
    public static string CreateDemoFolder(TempFolder temp, bool trimmed = false)
    {
        var root = DemoProject.Create(temp.SubFolder("projeto"));
        var folder = ProjectFolder.Open(root);
        if (trimmed)
        {
            // O smoke só precisa dos capítulos que as telas citam: 5 mil linhas em vez das 48 mil, que custam segundos e centenas de MB a cada abertura.
            var keep = DemoProject.Named.Select(n => n.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var key in DemoProject.Texts.Keys.Where(k => !keep.Contains(k)))
            {
                File.Delete(folder.TextPath(key));
            }
        }

        File.WriteAllText(folder.FormattingPath, """{ "wrap": { "enabled": true, "maxWidth": 40, "maxLines": 3, "charWidth": "uniform" } }""");
        var tones = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["美咲"] = "fala informal e animada; chama o Kenta pelo nome, sem honorífico",
            ["健太"] = "seco e resmungão; frases curtas",
            ["先生"] = "formal; chama os alunos pelo sobrenome",
        };
        SaveBible(folder);
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
    private static Func<string, ThemeVariant, Task<ScreenHost>> Characters(string? open = null, bool manyTraits = false, string? search = null) => async (_, theme) =>
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
            if (search is not null)
            {
                vm.Characters.SearchText = search;
                host.Settle();
            }

            if (open is not null)
            {
                vm.Characters.OpenCharacterCommand.Execute(vm.Characters.Items.Single(i => i.Name == open));
                host.Settle();
            }
        });
        return host;
    };

    /// <summary>A bíblia do jogo do projeto de demonstração: uma versão anterior guardada, duas cenas com campos editados, a sinopse, a linha do tempo e o custo da geração.</summary>
    private static void SaveBible(ProjectFolder folder)
    {
        folder.SaveBible(GameBible.From([new BibleScene("yst00012") { Summary = "Versão anterior." }]));
        folder.KeepPreviousBible();
        folder.SaveBible(GameBible.From(
        [
            new BibleScene("yst00012")
            {
                Name = "Atraso no portão",
                Summary = "Na chuva da manhã, Kenta chega ao portão da escola e encontra a professora de braços cruzados.",
                Characters = ["Kenta", "Misaki"],
                Tone = ["cômico leve", "tenso no fim"],
                Jokes = ["o guarda-chuva"],
                Relations = [new BibleRelation("Kenta e Misaki", "Amigos de infância, se provocam o tempo todo.")],
                Edited = [BibleField.Name],
            },
            new BibleScene("yst00013") { Name = "Volta para casa", Summary = "Kenta volta sozinho.", Jokes = ["o guarda-chuva"] },
        ]) with
        {
            Synopsis = "Kenta se muda para uma cidade onde chove todo semestre e descobre que um guarda-chuva perdido liga três colegas de turma.",
            Timeline = [new BibleTimelineEntry("Prólogo", "A chuva e a mudança de cidade"), new BibleTimelineEntry("Capítulo 1", "Um semestre começa molhado"), new BibleTimelineEntry("Capítulo 2", "O festival da escola")],
            Generation = new BibleGeneration(ScreenHost.Now, "claude-opus-5-5", 2, 1, 1.8m),
        });
        AiUsageLog.TryRecord(folder, new AiUsageTotals(0, 0, 0, 0, 0.8m, 1));
    }

    /// <summary>
    /// A aba Jogar do projeto de demonstração ligado a um jogo da Biblioteca, com o histórico de sessões e o Locale Emulator. Com <paramref name="playing"/>
    /// a partida abre pelo botão e fica em curso (o processo falso nunca sai), para a aba mostrar "Jogando agora" e o relógio.
    /// </summary>
    private static Func<string, ThemeVariant, Task<ScreenHost>> Play(bool playing) => async (root, theme) =>
    {
        var config = new TempFolder();
        var data = new TempFolder();
        var now = DateTimeOffset.Now;
        var gameFolder = data.SubFolder("jogo");
        File.WriteAllText(Path.Combine(gameFolder, "Game.exe"), "x");
        var emulator = data.SubFolder("LocaleEmulator");
        File.WriteAllText(Path.Combine(emulator, "LEProc.exe"), "x");
        var game = new LibraryGame("g1a2b3c4d", GameLibrary.NormalizeFolder(gameFolder), "Game.exe", new LibraryEngine("yuris", "Yu-RIS", LibraryEngineSource.Engine), "Amaoto no Hakoniwa", "雨音の箱庭", now)
        {
            ProjectPath = GameLibrary.NormalizeFolder(root),
            RequiresJapaneseLocale = true,
        };
        var folder = config.SubFolder("biblioteca");
        var playLog = new PlayLogFile(folder);
        foreach (var (daysAgo, hour, minute, minutes) in new[] { (0, 21, 14, 112), (1, 20, 2, 130), (4, 22, 40, 48), (6, 19, 30, 185), (12, 20, 0, 90), (20, 18, 30, 150), (33, 21, 0, 60), (47, 20, 15, 120), (61, 19, 0, 45) })
        {
            if (playing && daysAgo == 0)
            {
                continue;
            }

            var started = new DateTimeOffset(now.Date.AddDays(-daysAgo).AddHours(hour).AddMinutes(minute));
            playLog.Record(game.Id, new PlaySession(PlaySession.NewId(), started, started.AddMinutes(minutes), TimeSpan.FromMinutes(minutes), ViaLocaleEmulator: true));
        }

        var file = LibrarySamples.Save(folder, new GameLibrary([game], localeEmulatorPath: emulator));
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow - new TimeSpan(0, 42, 18));
        var options = new PlayTrackingOptions(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(10));
        var library = new LibraryViewModel(
            file, new FakeFolderPicker(null), action => action(), clock: playing ? clock : new FixedClock(now), readTranslation: _ => null,
            player: playing ? new GamePlayer(new StillRunningLauncher(), playLog, clock, options) : null, playLog: playLog);
        await library.LoadAsync();
        var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(now));
        var vm = new MainWindowViewModel(start, library: library) { Editor = new EditorViewModel(scriptLabel: DemoProject.Label) };
        var host = ScreenHost.Show(vm, theme, config, data);
        await Prepare(host, async () =>
        {
            await start.OpenFolderCommand.ExecuteAsync(null);
            await vm.ProjectOpening;
            vm.SelectedGameTab = vm.GameTabs.Single(t => t.Tab == GameTab.Play);
            if (playing)
            {
                _ = library.PlayCommand.ExecuteAsync(library.Cards.Single());
                var deadline = Environment.TickCount64 + 10_000;
                while (library.PlayingId is null && library.ErrorMessage is null && Environment.TickCount64 < deadline)
                {
                    await Task.Delay(10);
                }

                vm.Play.Session?.Tick();
                clock.Advance(TimeSpan.FromMinutes(2));
                vm.Play.Session?.Tick();
            }

            host.Settle();
        });
        return host;
    };

    private sealed class StillRunningLauncher : IGameLauncher
    {
        public IGameProcess Start(GameLaunch launch) => new Running();

        private sealed class Running : IGameProcess
        {
            public GameProcessState Poll() => GameProcessState.Running;

            public void Dispose()
            {
            }
        }
    }

    /// <summary>Uma aba dos Ajustes. A do VNDB precisa do serviço da lista, que o app monta e a captura troca pelo catálogo falso com um token válido.</summary>
    private static Func<string, ThemeVariant, Task<ScreenHost>> SettingsTab(int tab, bool vndb = false) => async (root, theme) =>
    {
        var host = await ScreenHost.OpenProjectAsync(root, theme);
        await Prepare(host, async () =>
        {
            await host.GoToAsync(NavKey.Settings);
            if (vndb)
            {
                var config = new TempFolder();
                var store = new MemoryVndbTokenStore();
                store.Save(FakeVndbCatalog.ValidToken);
                var service = new VndbListService(store, new FakeVndbCatalog(time: new FixedClock(ScreenHost.Now)), LibrarySamples.Save(config.Path, LibrarySamples.Library()), new FixedClock(ScreenHost.Now), _ => null);
                var connection = new VndbConnectionViewModel(service, new FixedClock(ScreenHost.Now));
                host.Vm.Settings.Vndb = connection;
                await connection.RefreshAsync();
                host.Own(config);
            }

            host.Vm.Settings.SelectedTab = tab;
            host.Settle();
        });
        return host;
    };

    /// <summary>
    /// A aba Ficha com o projeto de demonstração ligado ao VNDB e a ficha inventada de <see cref="GameSheetSamples"/> (sinopse, tags, personagens, dados), ou a
    /// revisão dela bloco a bloco. O vínculo é gravado na pasta do projeto e tirado quando a janela fecha, para as outras telas verem o projeto como era.
    /// </summary>
    private static Func<string, ThemeVariant, Task<ScreenHost>> Sheet(bool review, bool seeded = true) => async (root, theme) =>
    {
        var config = new TempFolder();
        if (seeded)
        {
            await GameSheetSamples.SeedAsync(config.Path);
        }

        var held = new LibraryGame("g1", "C:\\Jogos\\Amaoto", null, LibraryEngine.Unknown, "Amaoto no Hakoniwa", null, GameSheetSamples.Now) { VndbId = GameSheetSamples.Id };
        new LibraryFile(config.Path).Save(new GameLibrary([held]));
        new UserTagsFile(config.Path).Update(tags => tags.Add("g1", "favorito"));
        var folder = ProjectFolder.Open(root);
        folder.SaveVndbLink(new VndbLink(GameSheetSamples.Id, "Amaoto no Hakoniwa", "雨音の箱庭", null, []));
        var host = await ScreenHost.OpenProjectAsync(root, theme, start =>
        {
            var vm = new MainWindowViewModel(start) { Editor = new EditorViewModel(scriptLabel: DemoProject.Label) };
            vm.Sheet.ConfigDirectory = config.Path;
            vm.Sheet.CatalogFactory = GameSheetSamples.Catalog;
            vm.Sheet.TranslatorFactory = GameSheetSamples.Translator;
            return vm;
        });
        host.Own(config);
        host.Own(new Cleanup(() => File.Delete(folder.VndbPath)));
        await Prepare(host, async () =>
        {
            await host.Vm.Sheet.Loading;
            await host.GoToAsync(NavKey.Sheet);
            await host.Vm.Dashboard.ReloadAsync();
            if (review)
            {
                host.Vm.Sheet.StartReviewCommand.Execute(null);
                // Clicar de novo na aba Ficha durante a revisão volta para a ficha.
                host.Vm.SelectedGameTab = host.Vm.GameTabs.Single(t => t.Tab == GameTab.Sheet);
            }

            host.Settle();
        });
        return host;
    };

    private sealed class Cleanup(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

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
            "Numa cidade onde chove quase o ano inteiro, Kenta volta à escola em que o irmão mais velho estudou e encontra uma caixa de música que só toca quando alguém mente.", [], Rating: 79);
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

    private sealed class MemoryVndbTokenStore : IVndbTokenStore
    {
        private string? token;

        public string? Load() => token;

        public void Save(string value) => token = value;

        public bool Delete()
        {
            var had = token is not null;
            token = null;
            return had;
        }
    }

    /// <summary>Devolve a fonte de imagens do VNDB quando a janela da Biblioteca fecha.</summary>
    private sealed class RestoreImages(IVndbImageSource? previous) : IDisposable
    {
        public void Dispose() => VndbImages.Source = previous;
    }
}
