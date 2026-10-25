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
            var picker = new AvaloniaFolderPicker(window);
            var start = new StartViewModel(new RecentProjectsFile(configDirectory), picker, TimeProvider.System);
            var settings = new SettingsViewModel(new DpapiApiKeyStore(configDirectory), new AppSettingsFile(configDirectory));
            RequestedThemeVariant = (settings.SelectedTheme?.Value ?? ThemeChoice.System).ToVariant();
            settings.ThemeChanged += (_, choice) => RequestedThemeVariant = choice.ToVariant();
            var tasks = new BackgroundTaskService(a => Avalonia.Threading.Dispatcher.UIThread.Post(a));
            var suggester = LineSuggester.ForUser(configDirectory);
            var main = new MainWindowViewModel(start, ApiKeyStatus.Resolve(configDirectory), tasks, settings,
                () => new NewProjectWizardViewModel(picker, [new YuRisEngine()], tasks))
            {
                Editor = new EditorViewModel(suggester: suggester) { Explanations = LineExplanations.ForUser(configDirectory, () => settings.Rate) },
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
