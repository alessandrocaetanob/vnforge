using VNForge.Core;

namespace VNForge.Cli;

/// <summary>
/// Leitura de um arquivo do jogo (YPF, YBN) que sempre falha em português e com o caminho: o conteúdo fora do formato vira
/// <see cref="InvalidFileException"/> e o erro do disco, "Não foi possível ler". Exceção de lógica (bug) passa sem mudar.
/// </summary>
public static class InputFileGuard
{
    public static T Read<T>(string path, Func<T> read)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(read);

        try
        {
            return read();
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidFileException(path, "o arquivo termina antes do que a estrutura indica.", ex);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or FormatException or OverflowException)
        {
            throw new InvalidFileException(path, "o conteúdo não segue o formato esperado.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VNForgeException($"Não foi possível ler \"{path}\": {ex.Message}", ex);
        }
        catch (VNForgeException ex) when (!ex.Message.Contains(path, StringComparison.OrdinalIgnoreCase))
        {
            throw new VNForgeException($"{ex.Message} Arquivo: {path}", ex);
        }
    }
}
