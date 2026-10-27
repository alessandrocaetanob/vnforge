using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VNForge.AI.Claude;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Engines.YuRis;

namespace VNForge.App;

public sealed class App : Application
{
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
                () => new NewProjectWizardViewModel(picker, [new YuRisEngine()], tasks, appSettings))
            {
                Editor = new EditorViewModel(suggester: suggester, layoutMemory: new EditorLayoutMemory(configDirectory))
                {
                    Explanations = LineExplanations.ForUser(configDirectory, () => settings.Rate),
                    SuggestionPrice = unit => LineSuggester.PriceText(unit, LineSuggester.ReadModel(configDirectory), settings.Rate),
                },
                Glossary = { AutosaveClock = TimeProvider.System, Suggester = UserGlossarySuggester.ForUser(configDirectory) },
                TranslationProviderFactory = (model, effort) => ClaudeProviderFactory.Create(
                    ApiKeyStatus.Resolve(configDirectory).Key ?? throw new InvalidOperationException("Falta a chave da IA."),
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
