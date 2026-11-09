using VNForge.Installer;
using VNForge.Patching;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var arguments = InstallerArguments.Parse(args);
        var executable = Environment.ProcessPath ?? AppContext.BaseDirectory;
        var executableDirectory = Path.GetDirectoryName(executable);
        using var package = File.Exists(executable) ? InstallerBundle.TryOpen(executable) : null;
        if (arguments.UsesConsole)
        {
            using var output = ConsoleHost.OpenOutput();
            return InstallerApp.Run(arguments, output, package, Directory.GetCurrentDirectory(), executableDirectory);
        }

        return WindowHost.Run(args, arguments, package, executableDirectory);
    }
}
