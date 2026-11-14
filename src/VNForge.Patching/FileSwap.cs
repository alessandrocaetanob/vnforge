using System.Security.Cryptography;

namespace VNForge.Patching;

/// <summary>
/// A gravação conferida não deu certo: o disco devolveu um erro (cheio, arquivo preso, pasta somente leitura) ou terminou sem erro mas o destino relido não tem
/// o conteúdo gravado (vazio ou diferente). A mensagem é em português e diz que o que já estava gravado não foi alterado.
/// </summary>
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

    /// <summary>A falha do disco <paramref name="cause"/> ao gravar <paramref name="path"/>, na mensagem em português (a do sistema pode vir em inglês).</summary>
    public static FileWriteVerificationException ForFailedWrite(string path, Exception cause) =>
        new($"Não foi possível gravar '{path}': {cause.Message} O que já estava gravado não foi alterado.", cause);
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
    /// Move <paramref name="temporary"/> sobre <paramref name="path"/> e confere o resultado. <paramref name="move"/> é a troca em si (padrão: <see cref="ProjectDisk.Current"/>)
    /// e <paramref name="delete"/> apaga o destino antes dela no volume que não é local (padrão: <see cref="File.Delete(string)"/>); o Core passa as suas, com a espera por quem
    /// segura o arquivo. O temporário some em qualquer caso de sucesso.
    /// </summary>
    /// <exception cref="FileWriteVerificationException">O destino relido não bate com o temporário. Na troca com <c>.bak</c>, o conteúdo anterior já foi devolvido e a cópia ficou ao lado.</exception>
    public static void Replace(string temporary, string path, bool overwrite = true, Action<string, string, bool>? move = null, Action<string>? delete = null)
    {
        Replace(temporary, path, overwrite, Measure(temporary), move, delete);
    }

    // expected é o que o destino tem que ter depois da troca: lido uma vez só (o arquivo pode ter centenas de MB), no temporário ou na origem de uma cópia.
    private static void Replace(string temporary, string path, bool overwrite, Fingerprint expected, Action<string, string, bool>? move, Action<string>? delete)
    {
        move ??= ProjectDisk.Current.Move;
        delete ??= File.Delete;
        var existed = File.Exists(path);
        if (ProjectDisk.Current.IsLocal(path) || !existed || !overwrite)
        {
            try
            {
                move(temporary, path, overwrite);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && e is not FileWriteVerificationException && (overwrite || !File.Exists(path)))
            {
                // Sem overwrite, um destino que já existe é a recusa que quem chama trata (criar projeto onde há um): essa sobe como está.
                throw FileWriteVerificationException.ForFailedWrite(path, e);
            }

            try
            {
                Verify(path, expected, $"O arquivo '{path}'");
            }
            catch (FileWriteVerificationException e) when (!existed)
            {
                // O destino não existia: o que está lá é só o que esta gravação criou errado (vazio, truncado), e um arquivo de projeto vazio é pior do que nenhum.
                DeleteQuietly(path);
                throw new FileWriteVerificationException(File.Exists(path) ? e.Message : $"{e.Message} O arquivo estragado foi removido.", e);
            }

            return;
        }

        SwapWithBackup(temporary, path, expected, move, delete);
    }

    /// <summary>
    /// Copia <paramref name="source"/> para <paramref name="destination"/> por um temporário ao lado e <see cref="Replace"/>: o destino nunca fica pela metade nem vazio sem erro.
    /// O destino é conferido contra a origem (lida uma vez), porque um disco que não é local pode devolver a cópia vazia.
    /// </summary>
    public static void Copy(string source, string destination, Action<string, string, bool>? move = null, Action<string>? delete = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            var expected = Measure(source);
            try
            {
                ProjectDisk.Current.Copy(source, temporary);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw FileWriteVerificationException.ForFailedWrite(destination, e);
            }

            Replace(temporary, destination, overwrite: true, expected, move, delete);
        }
        finally
        {
            DeleteQuietly(temporary);
        }
    }

    /// <summary>
    /// <see cref="File.Exists(string)"/> que antes devolve um arquivo que uma queda deixou só no <c>.bak</c>: se o processo caiu entre apagar o destino e pôr o novo no lugar,
    /// o <c>{nome}.{guid}.bak</c> mais novo ao lado volta a ser o arquivo. Sem isso o <c>vnforge.json</c> sumia e o projeto parecia não existir.
    /// </summary>
    /// <exception cref="FileWriteVerificationException">O <c>.bak</c> não pôde ser devolvido ou voltou diferente; ele continua ao lado.</exception>
    public static bool ExistsOrRecover(string path)
    {
        if (File.Exists(path))
        {
            return true;
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return false;
        }

        var name = Path.GetFileName(fullPath);
        var backup = Directory.EnumerateFiles(directory, $"{name}.*.bak")
            .Where(file => IsBackupOf(Path.GetFileName(file), name))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenByDescending(File.GetCreationTimeUtc)
            .FirstOrDefault();
        if (backup is null)
        {
            return false;
        }

        var expected = Measure(backup);
        try
        {
            File.Copy(backup, fullPath, overwrite: false);
        }
        catch (IOException) when (File.Exists(fullPath))
        {
            // Outro processo devolveu o arquivo primeiro.
            return true;
        }

        try
        {
            Verify(fullPath, expected, $"O arquivo '{fullPath}'");
        }
        catch (FileWriteVerificationException e)
        {
            DeleteQuietly(fullPath);
            throw new FileWriteVerificationException($"{e.Message} A cópia de segurança continua em '{backup}'.", e);
        }

        DeleteQuietly(backup);
        return true;
    }

    private static bool IsBackupOf(string fileName, string name)
    {
        var middle = fileName.AsSpan(name.Length + 1, fileName.Length - name.Length - 1 - ".bak".Length);
        return middle.Length == 32 && middle.ToString().All(Uri.IsHexDigit);
    }

    private static void SwapWithBackup(string temporary, string path, Fingerprint expected, Action<string, string, bool> move, Action<string> delete)
    {
        // Nome único: um X.bak que já exista ao lado (na pasta do jogo, arquivo do usuário) nunca é sobrescrito nem apagado.
        var backup = $"{path}.{Guid.NewGuid():N}.bak";
        try
        {
            ProjectDisk.Current.Copy(path, backup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(backup);
            throw FileWriteVerificationException.ForFailedWrite(path, e);
        }

        // Só o tamanho: o SHA-256 do original e da cópia seria mais duas leituras do arquivo inteiro a cada gravação. A leitura completa fica para o caminho de falha (Restore).
        if (new FileInfo(backup).Length != new FileInfo(path).Length)
        {
            throw new FileWriteVerificationException($"A cópia de segurança de '{path}' não confere com o original; o arquivo não foi alterado.");
        }

        try
        {
            delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (Unchanged(path, backup))
            {
                // O Delete falhou (arquivo preso): o destino é o de antes e a cópia não serve de nada.
                DeleteQuietly(backup);
                throw new FileWriteVerificationException($"Não foi possível gravar '{path}': {e.Message} O arquivo não foi alterado.", e);
            }

            Restore(path, backup, e, delete);
            throw new FileWriteVerificationException(
                $"Não foi possível gravar '{path}': {e.Message} O conteúdo anterior foi devolvido ao arquivo e a cópia de segurança ficou em '{backup}'.", e);
        }

        try
        {
            move(temporary, path, false);
            Verify(path, expected, $"O arquivo '{path}'");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Restore(path, backup, e, delete);
            throw new FileWriteVerificationException(
                $"Não foi possível gravar '{path}': {e.Message} O conteúdo anterior foi devolvido ao arquivo e a cópia de segurança ficou em '{backup}'.", e);
        }

        DeleteQuietly(backup);
    }

    private static bool Unchanged(string path, string backup)
    {
        try
        {
            return File.Exists(path) && Measure(path) == Measure(backup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Restore(string path, string backup, Exception cause, Action<string> delete)
    {
        try
        {
            var old = Measure(backup);
            delete(path);
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
