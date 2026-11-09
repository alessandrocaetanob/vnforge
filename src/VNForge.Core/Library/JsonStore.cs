namespace VNForge.Core.Library;

/// <summary>
/// A mecânica que os arquivos pequenos do usuário (notas, índice de capturas) compartilham: ler recusando arquivo ilegível, e mudar sob uma trava <c>.lock</c> ao lado,
/// gravando por <see cref="AtomicFile"/>. Quem usa dá o formato (<paramref name="parse"/> e <paramref name="serialize"/>). <paramref name="subject"/> completa
/// "Não foi possível gravar ..." (as notas, o índice das capturas).
/// </summary>
internal sealed class JsonStore<TModel>(string path, string subject, string busyMessage, Func<TModel> empty, Func<string, string, TModel> parse, Func<TModel, string> serialize)
{
    public string Path { get; } = path;

    /// <exception cref="LibraryException">O arquivo existe e não pôde ser lido ou não segue o formato.</exception>
    public TModel Load()
    {
        if (!File.Exists(Path))
        {
            return empty();
        }

        try
        {
            return parse(File.ReadAllText(Path), Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException($"O arquivo '{Path}' não pôde ser lido: {e.Message}", e);
        }
    }

    /// <exception cref="LibraryException">O arquivo está ilegível, não pôde ser gravado, ou outro processo o segurou por mais de <see cref="LibraryFile.LockTimeout"/>.</exception>
    public TResult Update<TResult>(Func<TModel, TResult> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        using var gate = FileLock.Acquire(Path + ".lock", LibraryFile.LockTimeout, busyMessage);
        var model = Load();
        var result = change(model);
        Save(model);
        return result;
    }

    public void Save(TModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        try
        {
            AtomicFile.WriteAllText(Path, serialize(model));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException($"Não foi possível gravar {subject} em '{Path}': {e.Message}", e);
        }
    }
}
