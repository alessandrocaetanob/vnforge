using System.Text;
using VNForge.Core.Projects;
using VNForge.Patching;

namespace VNForge.Core.Library;

/// <summary>
/// A gravação de arquivos do projeto e da biblioteca: escreve num temporário ao lado e o põe no lugar por <see cref="FileSwap"/>, que troca, relê o destino e
/// usa o caminho com <c>.bak</c> em volume que não é local (a causa e a estratégia estão lá). Destino vazio ou diferente lança <see cref="FileWriteVerificationException"/>,
/// uma <see cref="IOException"/>: os catches de IOException do Core (rollback da injeção, laço do restore, gasto da IA) tratam a falha como qualquer falha de disco.
/// </summary>
internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8SemBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Grava o texto (UTF-8 sem BOM). <paramref name="wait"/> é a espera entre as tentativas da troca (padrão: dormir); <paramref name="overwrite"/> falso recusa um destino existente.</summary>
    public static void WriteAllText(string path, string text, Action<int>? wait = null, bool overwrite = true, Func<string>? newTemporaryPath = null) =>
        WriteAllBytes(path, Utf8SemBom.GetBytes(text), wait, overwrite, newTemporaryPath);

    /// <summary>Como <see cref="WriteAllText"/>, para bytes (uma imagem não pode aparecer pela metade).</summary>
    public static void WriteAllBytes(string path, byte[] bytes, Action<int>? wait = null, bool overwrite = true, Func<string>? newTemporaryPath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // newTemporaryPath existe para o teste prender o temporário antes da escrita: o nome real leva um Guid que ninguém conhece de antemão.
        var temporary = newTemporaryPath?.Invoke() ?? $"{path}.{Guid.NewGuid():N}.tmp";
        var sleep = wait ?? Thread.Sleep;
        try
        {
            try
            {
                ProjectFileIo.Retry<int>(null, () =>
                {
                    ProjectDisk.Current.WriteAllBytes(temporary, bytes);
                    return 0;
                }, sleep);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw FileWriteVerificationException.ForFailedWrite(path, e);
            }

            FileSwap.Replace(temporary, path, bytes, overwrite, (source, destination, over) => ProjectFileIo.Move(source, destination, over, sleep), file => ProjectFileIo.Delete(file, sleep));
        }
        finally
        {
            ProjectFileIo.DeleteQuietly(temporary);
        }
    }

    /// <summary>
    /// Põe o temporário já escrito no lugar de <paramref name="path"/>, esperando quem estiver segurando o arquivo e conferindo o resultado.
    /// Quem grava por conta própria (um arquivo grande, um fluxo) chama isto no fim.
    /// </summary>
    /// <exception cref="FileWriteVerificationException">O destino relido não bate com o que foi gravado.</exception>
    public static void Replace(string temporary, string path, bool overwrite = true, Action<int>? wait = null)
    {
        var sleep = wait ?? Thread.Sleep;
        FileSwap.Replace(temporary, path, overwrite, (source, destination, over) => ProjectFileIo.Move(source, destination, over, sleep), file => ProjectFileIo.Delete(file, sleep));
    }

    /// <summary>Copia <paramref name="source"/> sobre <paramref name="destination"/> pelo mesmo caminho conferido: a cópia vazia ou diferente da origem vira <see cref="FileWriteVerificationException"/>.</summary>
    public static void Copy(string source, string destination, Action<int>? wait = null)
    {
        var sleep = wait ?? Thread.Sleep;
        FileSwap.Copy(
            source,
            destination,
            (from, to, over) => ProjectFileIo.Move(from, to, over, sleep),
            file => ProjectFileIo.Delete(file, sleep),
            (from, to, over) => ProjectFileIo.Retry<int>(null, () =>
            {
                ProjectDisk.Current.Copy(from, to, over);
                return 0;
            }, sleep));
    }
}
