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
using VNForge.Core.Jmdict;
using VNForge.Core.Memory;
using VNForge.Core.Projects;
using VNForge.Engines.YuRis;

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
            RequestedThemeVariant = (settings.SelectedTheme?.Value ?? ThemeChoice.System).ToVariant();
            settings.ThemeChanged += (_, choice) => RequestedThemeVariant = choice.ToVariant();
            var tasks = new BackgroundTaskService(a => Avalonia.Threading.Dispatcher.UIThread.Post(a));
            var suggester = LineSuggester.ForUser(configDirectory);
            var main = new MainWindowViewModel(start, ApiKeyStatus.Resolve(configDirectory), tasks, settings,
                () => new NewProjectWizardViewModel(picker, [new YuRisEngine()], tasks, appSettings, vndb: VNForge.Vndb.VndbClient.Create(Path.Combine(configDirectory, "vndb-cache"))) { PersonalGlossary = new PersonalGlossary(configDirectory) })
            {
                Editor = new EditorViewModel(suggester: suggester, layoutMemory: new EditorLayoutMemory(configDirectory))
                {
                    Memory = new TranslationMemory(configDirectory),
                    Dictionary = new OfflineDictionary(new JmdictStore(Path.Combine(configDirectory, "dictionary")), postToUi: a => Avalonia.Threading.Dispatcher.UIThread.Post(a)),
                    Explanations = LineExplanations.ForUser(configDirectory, () => settings.Rate),
                    SuggestionPrice = unit => LineSuggester.PriceText(unit, LineSuggester.ReadModel(configDirectory), settings.Rate),
                },
                Release = { Picker = picker },
                Images =
                {
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
            window.DataContext = main;
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await start.LoadAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
