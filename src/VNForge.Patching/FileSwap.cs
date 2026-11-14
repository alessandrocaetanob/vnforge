using System.Security.Cryptography;

namespace VNForge.Patching;

/// <summary>A troca de um arquivo terminou sem erro do disco, mas o destino relido não tem o conteúdo gravado (vazio ou diferente).</summary>
public sealed class FileWriteVerificationException : IOException
{
    public FileWriteVerificationException(string message)
        : base(message)
    {
    }

    public FileWriteVerificationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// O único caminho que põe um arquivo já escrito num temporário no lugar do destino, no projeto e na pasta do jogo: troca e depois relê o destino comparando com o
/// temporário (SHA-256 e tamanho; o tamanho sozinho mente, porque o stat do rclone dizia 730 bytes e a leitura terminava antes). Destino vazio ou diferente lança
/// <see cref="FileWriteVerificationException"/>, nunca sucesso. Num volume que não é local (<see cref="IProjectDisk.IsLocal"/>) o rename sobre um arquivo existente
/// não é confiável: o rclone com cache em modo full devolve sucesso e deixa o destino com 0 byte (vfs/file.go trata a falha do cache.Rename como INFO).
/// Ali o destino é copiado para <c>.bak</c>, apagado, o temporário entra com Move sem sobrescrever, e o <c>.bak</c> só sai depois da conferência.
/// </summary>
public static class FileSwap
{
    /// <summary>
    /// Move <paramref name="temporary"/> sobre <paramref name="path"/> e confere o resultado. <paramref name="move"/> é a troca em si (padrão: <see cref="ProjectDisk.Current"/>);
    /// o Core passa a sua, com a espera por quem segura o arquivo. O temporário some em qualquer caso de sucesso.
    /// </summary>
    /// <exception cref="FileWriteVerificationException">O destino relido não bate com o temporário. Na troca com <c>.bak</c>, o conteúdo anterior já foi devolvido e a cópia ficou ao lado.</exception>
    public static void Replace(string temporary, string path, bool overwrite = true, Action<string, string, bool>? move = null)
    {
        move ??= ProjectDisk.Current.Move;
        var expected = Measure(temporary);
        if (ProjectDisk.Current.IsLocal(path) || !File.Exists(path) || !overwrite)
        {
            move(temporary, path, overwrite);
            Verify(path, expected, $"O arquivo '{path}'");
            return;
        }

        SwapWithBackup(temporary, path, expected, move);
    }

    /// <summary>Copia <paramref name="source"/> para <paramref name="destination"/> por um temporário ao lado e <see cref="Replace"/>: o destino nunca fica pela metade nem vazio sem erro.</summary>
    public static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(source, temporary, overwrite: true);
            Replace(temporary, destination);
        }
        finally
        {
            DeleteQuietly(temporary);
        }
    }

    private static void SwapWithBackup(string temporary, string path, Fingerprint expected, Action<string, string, bool> move)
    {
        // Nome único: um X.bak que já exista ao lado (na pasta do jogo, arquivo do usuário) nunca é sobrescrito nem apagado.
        var backup = $"{path}.{Guid.NewGuid():N}.bak";
        File.Copy(path, backup, overwrite: true);
        if (Measure(backup) != Measure(path))
        {
            throw new FileWriteVerificationException($"A cópia de segurança de '{path}' não confere com o original; o arquivo não foi alterado.");
        }

        var old = Measure(backup);
        try
        {
            File.Delete(path);
            move(temporary, path, false);
            Verify(path, expected, $"O arquivo '{path}'");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Restore(path, backup, old, e);
            throw new FileWriteVerificationException(
                $"Não foi possível gravar '{path}': {e.Message} O conteúdo anterior foi devolvido ao arquivo e a cópia de segurança ficou em '{backup}'.", e);
        }

        DeleteQuietly(backup);
    }

    private static void Restore(string path, string backup, Fingerprint old, Exception cause)
    {
        try
        {
            File.Delete(path);
            File.Copy(backup, path, overwrite: false);
            Verify(path, old, $"O arquivo '{path}'");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new FileWriteVerificationException(
                $"Não foi possível gravar '{path}' ({cause.Message}) e o conteúdo anterior também não pôde ser devolvido ({e.Message}). A cópia de segurança está em '{backup}'.", e);
        }
    }

    private static void Verify(string path, Fingerprint expected, string subject)
    {
        Fingerprint actual;
        try
        {
            actual = Measure(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new FileWriteVerificationException($"{subject} não pôde ser relido depois de gravado ({e.Message}). O disco pode não estar guardando o que foi gravado.", e);
        }

        if (actual != expected)
        {
            throw new FileWriteVerificationException(
                $"{subject} ficou com {actual.Length} bytes depois de gravado, e o esperado eram {expected.Length}: o disco devolveu um conteúdo diferente do gravado.");
        }
    }

    // Lê o fluxo até o fim em vez de confiar em FileInfo.Length: num disco que mente, o tamanho existe e a leitura termina antes.
    private static Fingerprint Measure(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            length += read;
        }

        return new Fingerprint(length, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // O temporário ou a cópia que sobra é só lixo na pasta.
        }
    }

    private readonly record struct Fingerprint(long Length, string Sha256);
}
