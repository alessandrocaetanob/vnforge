using VNForge.Patching;

namespace VNForge.Cli.Tests;

/// <summary>
/// Um disco de rede falso: não é local e o rename dá certo mas deixa o destino com 0 byte (o que o rclone com cache em modo full fez no projeto real).
/// Só zera os destinos que <paramref name="zeroes"/> aceita.
/// </summary>
internal sealed class ZeroingDisk(Func<string, bool> zeroes) : IProjectDisk
{
    public bool IsLocal(string path) => false;

    public void Move(string source, string destination, bool overwrite)
    {
        File.Move(source, destination, overwrite);
        if (zeroes(destination))
        {
            File.WriteAllBytes(destination, []);
        }
    }
}
