using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Threading;
using VNForge.Installer.ViewModels;
using VNForge.Patching;

namespace VNForge.Installer;

/// <summary>
/// Sobe o Avalonia com as janelas. Fica numa classe à parte, e sem inline, para o modo de console nunca carregar o Avalonia: o instalador
/// de console, copiado sem as bibliotecas de janela, ainda roda.
/// </summary>
internal static class WindowHost
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(string[] args, InstallerArguments arguments, PatchPackage? package, string? executableDirectory)
    {
        InstallerApplication.Model = new InstallerViewModel(
            package is null ? null : new PatchEngine(package),
            new SystemShell(),
            new InstallerStart(arguments.Folder, Directory.GetCurrentDirectory(), executableDirectory),
            post: action => Dispatcher.UIThread.Post(action));
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<InstallerApplication>()
            .UsePlatformDetect()
            .LogToTrace();
}
