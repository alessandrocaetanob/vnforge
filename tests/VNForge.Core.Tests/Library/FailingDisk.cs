using System.Text.RegularExpressions;
using VNForge.Patching;

namespace VNForge.Core.Tests.Library;

/// <summary>Como o disco falha no mundo real. Cada tipo age no ponto onde a falha acontece de verdade.</summary>
public enum DiskFailure
{
    /// <summary>O rename devolve sucesso e deixa o destino com 0 byte (rclone com cache em modo full).</summary>
    ZeroedRename,

    /// <summary>Outro processo segura o destino: a troca dá violação de compartilhamento.</summary>
    Locked,

    /// <summary>O disco enche no meio da escrita do temporário: metade dos bytes chega e a escrita falha.</summary>
    DiskFull,

    /// <summary>A pasta é somente leitura: criar o temporário dá acesso negado.</summary>
    ReadOnly,

    /// <summary>A escrita do temporário termina sem erro com metade dos bytes.</summary>
    TruncatedWrite,
}

/// <summary>
/// O <see cref="IProjectDisk"/> do teste: falha como <paramref name="failure"/> só nos arquivos que <paramref name="fails"/> aceita (o caminho final do arquivo, não o do temporário),
/// e deixa o resto no disco real. As mensagens são as do sistema em inglês, para o teste provar que o erro que chega à pessoa é o do VNForge, em português.
/// </summary>
internal sealed partial class FailingDisk(DiskFailure failure, bool local, Func<string, bool> fails) : IProjectDisk
{
    public const string SystemMessage = "The process cannot access the file because it is being used by another process.";

    public bool IsLocal(string path) => local;

    public void Move(string source, string destination, bool overwrite)
    {
        if (!fails(destination))
        {
            File.Move(source, destination, overwrite);
            return;
        }

        switch (failure)
        {
            case DiskFailure.ZeroedRename:
                File.Move(source, destination, overwrite);
                File.WriteAllBytes(destination, []);
                break;
            case DiskFailure.Locked:
                throw new IOException(SystemMessage, unchecked((int)0x80070020));
            default:
                File.Move(source, destination, overwrite);
                break;
        }
    }

    public void WriteAllBytes(string path, byte[] bytes)
    {
        if (!FailTemporary(path, () => File.WriteAllBytes(path, bytes[..(bytes.Length / 2)])))
        {
            File.WriteAllBytes(path, bytes);
        }
    }

    public void Copy(string source, string destination, bool overwrite)
    {
        var handled = FailTemporary(destination, () =>
        {
            var bytes = File.ReadAllBytes(source);
            File.WriteAllBytes(destination, bytes[..(bytes.Length / 2)]);
        });
        if (!handled)
        {
            File.Copy(source, destination, overwrite);
        }
    }

    // O disco cheio, a pasta somente leitura e a escrita truncada atuam no temporário, antes de o destino ser tocado; devolve verdadeiro quando a escrita foi tomada pela falha.
    private bool FailTemporary(string temporary, Action writePartial)
    {
        if (!TemporaryName().IsMatch(temporary) || !fails(TemporaryName().Replace(temporary, string.Empty)))
        {
            return false;
        }

        switch (failure)
        {
            case DiskFailure.DiskFull:
                writePartial();
                throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));
            case DiskFailure.ReadOnly:
                throw new UnauthorizedAccessException($"Access to the path '{temporary}' is denied.");
            case DiskFailure.TruncatedWrite:
                writePartial();
                return true;
            default:
                return false;
        }
    }

    [GeneratedRegex(@"\.[0-9a-f]{32}\.tmp$")]
    private static partial Regex TemporaryName();
}
