using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VNForge.Installer.ViewModels;
using VNForge.Installer.Views;

namespace VNForge.Installer;

public sealed class InstallerApplication : Application
{
    /// <summary>O estado que a janela mostra; o <c>Program</c> o põe antes de iniciar o Avalonia.</summary>
    public static InstallerViewModel? Model { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Model is { } model)
        {
            var window = new InstallerWindow { DataContext = model };
            window.Opened += async (_, _) => await model.StartAsync();
            desktop.MainWindow = window;
            desktop.Exit += (_, e) => e.ApplicationExitCode = model.ExitCode;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
