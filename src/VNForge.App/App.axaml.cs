using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;

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
            var configDirectory = RecentProjectsFile.DefaultDirectory();
            var window = new MainWindow();
            var start = new StartViewModel(new RecentProjectsFile(configDirectory), new AvaloniaFolderPicker(window), TimeProvider.System);
            var settings = new SettingsViewModel(new DpapiApiKeyStore(configDirectory), new AppSettingsFile(configDirectory));
            window.DataContext = new MainWindowViewModel(start, ApiKeyStatus.Resolve(configDirectory), settings);
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await start.LoadAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
