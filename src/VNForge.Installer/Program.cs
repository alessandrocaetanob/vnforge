using VNForge.Installer;
using VNForge.Patching;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var executable = Environment.ProcessPath ?? AppContext.BaseDirectory;
using var package = File.Exists(executable) ? InstallerBundle.TryOpen(executable) : null;
return InstallerApp.Run(args, Console.In, Console.Out, package, Directory.GetCurrentDirectory(), Path.GetDirectoryName(executable));
