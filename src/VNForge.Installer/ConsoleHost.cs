using System.Runtime.InteropServices;
using System.Text;

namespace VNForge.Installer;

/// <summary>
/// O instalador é um programa de janela, que no Windows nasce sem console. Para o modo de console (<c>--silencioso</c>, <c>--ajuda</c>) escrever no terminal
/// de quem o chamou, ele se liga ao console do processo pai. Com a saída redirecionada (um teste, um script) os identificadores já vêm herdados e nada muda.
/// </summary>
internal static partial class ConsoleHost
{
    private const int AttachParentProcess = -1;

    public static TextWriter OpenOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            AttachConsole(AttachParentProcess);
        }

        return new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}
