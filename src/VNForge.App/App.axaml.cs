using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using VNForge.AI.Claude;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Factsheet;
using VNForge.Core.Jmdict;
using VNForge.Core.Library;
using VNForge.Core.Memory;
using VNForge.Core.Projects;
using VNForge.Engines.YuRis;
using VNForge.Platform.Windows;

namespace VNForge.App;

public sealed class App : Application
{
    // O quadro é desenhado pelo navegador em tons de cinza; o Skia do Avalonia suaviza o texto por subpixel (franjas coloridas), que engrossa o traço
    // no mesmo corpo e peso. A propriedade herda pela árvore visual, então a janela basta; popups e diálogos na camada de sobreposição herdam dela.
    static App() => Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => TextOptions.SetTextRenderingMode(window, TextRenderingMode.Antialias));

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#if DEBUG
            if (Environment.GetCommandLineArgs().Contains(Gallery.ComponentGalleryWindow.Flag))
            {
                desktop.MainWindow = new Gallery.ComponentGalleryWindow();
                base.OnFrameworkInitializationCompleted();
                return;
            }
#endif

            var configDirectory = RecentProjectsFile.DefaultDirectory();
            VndbImages.Source = VndbImages.CreateDefault(configDirectory);
            var window = new MainWindow();
            Controls.TechnicalDetails.SetMemory(window, new TechnicalDetailsMemory(configDirectory));
            var picker = new AvaloniaFolderPicker(window);
            var start = new StartViewModel(new RecentProjectsFile(configDirectory), picker, TimeProvider.System);
            var appSettings = new AppSettingsFile(configDirectory);
            var settings = new SettingsViewModel(new DpapiApiKeyStore(configDirectory), appSettings);
            settings.LocaleEmulator = new LocaleEmulatorSettingsViewModel(new LibraryFile(configDirectory), picker);
            RequestedThemeVariant = (settings.SelectedTheme?.Value ?? ThemeChoice.System).ToVariant();
            settings.ThemeChanged += (_, choice) => RequestedThemeVariant = choice.ToVariant();
            var tasks = new BackgroundTaskService(a => Avalonia.Threading.Dispatcher.UIThread.Post(a));
            var suggester = LineSuggester.ForUser(configDirectory);
            // Um só cliente: o limite de requisições por segundo do VNDB vale para a Biblioteca e para o assistente de novo projeto juntos.
            var vndb = VNForge.Vndb.VndbClient.Create(Path.Combine(configDirectory, "vndb-cache"));
            var library = CreateLibrary(configDirectory, picker, [new YuRisEngine()], a => Avalonia.Threading.Dispatcher.UIThread.Post(a), vndb);
            // O token do VNDB vai para o cofre do Windows, como a chave da Anthropic; a lista mora no mesmo library.json.
            var vndbList = new VndbListService(new VNForge.Vndb.DpapiVndbTokenStore(configDirectory), vndb, new LibraryFile(configDirectory), settings: new VndbSyncSettingsFile(configDirectory));
            library.ListService = vndbList;
            if (VndbImages.Source is { } imageSource)
            {
                library.FollowPrivacy(imageSource);
            }

            settings.Vndb = new VndbConnectionViewModel(vndbList);
            var main = new MainWindowViewModel(start, ApiKeyStatus.Resolve(configDirectory), tasks, settings,
                () => new NewProjectWizardViewModel(picker, [new YuRisEngine()], tasks, appSettings, vndb: vndb) { PersonalGlossary = new PersonalGlossary(configDirectory) },
                library: library)
            {
                Editor = new EditorViewModel(suggester: suggester, layoutMemory: new EditorLayoutMemory(configDirectory))
                {
                    Memory = new TranslationMemory(configDirectory),
                    Dictionary = new OfflineDictionary(new JmdictStore(Path.Combine(configDirectory, "dictionary")), postToUi: a => Avalonia.Threading.Dispatcher.UIThread.Post(a)),
                    Explanations = LineExplanations.ForUser(configDirectory, () => settings.Rate),
                    SuggestionPrice = unit => LineSuggester.PriceText(unit, LineSuggester.ReadModel(configDirectory), settings.Rate),
                },
                Release = { Picker = picker },
                Sheet =
                {
                    ConfigDirectory = configDirectory,
                    CatalogFactory = () => vndb,
                    TranslatorFactory = () => ClaudeProviderFactory.CreateSheet(
                        ApiKeyStatus.Resolve(configDirectory).Key ?? throw new InvalidOperationException("Falta a chave da IA."),
                        new ClaudeOptions { Model = LineSuggester.ReadModel(configDirectory), Effort = ClaudeEffort.Medium }),
                },
                Play =
                {
                    CapturesFile = new GameCapturesFile(GameCapturesFile.DefaultDirectory()),
                    NotesFile = new GameNotesFile(configDirectory),
                    Capturer = new VNForge.Platform.Windows.WindowCapturer(),
                    Hotkey = OperatingSystem.IsWindows() ? new VNForge.Platform.Windows.GlobalHotkey() : null,
                    PostToUi = a => Avalonia.Threading.Dispatcher.UIThread.Post(a),
                },
                Images =
                {
                    HasApiKey = () => ApiKeyStatus.Resolve(configDirectory).Key is not null,
                    ReaderFactory = model => ClaudeProviderFactory.CreateImageReader(
                        ApiKeyStatus.Resolve(configDirectory).Key ?? throw new InvalidOperationException("Falta a chave da IA."),
                        new ClaudeOptions { Model = model, Effort = ClaudeEffort.Low, MaxTokens = 2000 }),
                },
                Glossary = { AutosaveClock = TimeProvider.System, PersonalStore = new PersonalGlossary(configDirectory), Suggester = UserGlossarySuggester.ForUser(configDirectory) },
                TranslationProviderFactory = (model, effort) => ClaudeProviderFactory.Create(
                    ApiKeyStatus.Resolve(configDirectory).Key ?? throw new InvalidOperationException("Falta a chave da IA."),
                    new ClaudeOptions { Model = model, Effort = effort }),
                EditorPassProviderFactory = (model, effort) => ClaudeProviderFactory.CreateEditorPass(
                    ApiKeyStatus.Resolve(configDirectory).Key ?? throw new InvalidOperationException("Falta a chave da IA."),
                    new ClaudeOptions { Model = model, Effort = effort }),
                MessageBatchClientFactory = () => ClaudeProviderFactory.CreateBatchClient(
                    ApiKeyStatus.Resolve(configDirectory).Key ?? throw new InvalidOperationException("Falta a chave da IA.")),
                JudgeProviderFactory = model => ClaudeProviderFactory.CreateJudge(
                    ApiKeyStatus.Resolve(configDirectory).Key ?? throw new InvalidOperationException("Falta a chave da IA."),
                    new ClaudeOptions { Model = model, Effort = ClaudeEffort.Low }),
                BibleProviderFactory = (model, effort) => ClaudeProviderFactory.CreateBible(
                    ApiKeyStatus.Resolve(configDirectory).Key ?? throw new InvalidOperationException("Falta a chave da IA."),
                    new ClaudeOptions { Model = model, Effort = effort }),
                BatchRequestProviderFactory = (model, effort) => new ClaudeBatchRequestProvider(
                    ClaudeProviderFactory.CreateClient(ApiKeyStatus.Resolve(configDirectory).Key ?? throw new InvalidOperationException("Falta a chave da IA.")),
                    new ClaudeOptions { Model = model, Effort = effort }),
            };
            main.Conference.Shortener = suggester;
            settings.Vndb.ListChanged += (_, _) =>
            {
                _ = library.ReloadListAsync();
                main.Sheet.ListCard?.Refresh();
            };
            window.DataContext = main;
            desktop.MainWindow = window;
            // Os recentes primeiro: a Biblioteca liga cada projeto de tradução ao jogo dele por essa lista.
            window.Opened += async (_, _) => await main.Library.OpenAsync(() => start.LoadAsync());
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>A Biblioteca do app: o mesmo <c>library.json</c> da CLI e do cache de imagens, a varredura e o casamento com o VNDB pelo cliente com cache que o chamador passa.</summary>
    internal static LibraryViewModel CreateLibrary(string configDirectory, IFolderPicker picker, IReadOnlyList<VNForge.Core.Engines.IEngine> engines, Action<Action> post, VNForge.Core.Vndb.IVndbCatalog vndb, IGameLauncher? launcher = null)
    {
        var file = new LibraryFile(configDirectory);
        var playLog = new PlayLogFile(configDirectory);
        LibraryViewModel? library = null;
        var scan = new LibraryScanService(
            file,
            new LibraryScanner(new EngineRecognizer(engines), new GameWindowTitleReader()),
            () => new LibraryMatcher(vndb),
            () => library?.RecentProjects() ?? []);
        library = new LibraryViewModel(
            file,
            picker,
            post,
            scan,
            new LibraryEditor(file, () => new LibraryMatcher(vndb)),
            sheets: new SheetCatalogSource(new SheetStore(configDirectory)),
            openFolder: MainWindowViewModel.OpenInFileManager,
            tagsFile: new UserTagsFile(configDirectory),
            player: new GamePlayer(launcher ?? new ProcessGameLauncher(), playLog, warn: message => post(() => library?.ReportProblem(message))),
            playLog: playLog,
            switcher: new LibraryTranslationSwitcher(new GameTester(engines, new ShellGameProcessStarter())),
            engines: engines,
            portugueseChecker: () => new PortugueseTranslationChecker(vndb));
        library.Export.OpenFile = MainWindowViewModel.OpenInFileManager;
        return library;
    }
}
