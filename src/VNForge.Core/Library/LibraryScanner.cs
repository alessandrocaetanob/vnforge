namespace VNForge.Core.Library;

/// <summary>Como a varredura anda. <paramref name="MaxDepth"/> conta as pastas abaixo da raiz (0 olha só a raiz).</summary>
/// <param name="IncludeUnknown">Também lista pasta com executável e nenhuma assinatura conhecida, com a engine <c>unknown</c>. Fora isso, pasta sem engine reconhecida não é jogo.</param>
/// <param name="EstimateFolders">Antes de varrer, conta as pastas só pelos nomes (sem descer dentro de pasta que tem executável) para o andamento dizer "214 de 380 pastas". Custa uma passada a mais, então a CLI não liga.</param>
public sealed record LibraryScanOptions(int MaxDepth = 6, bool IncludeUnknown = false, bool EstimateFolders = false);

/// <summary>
/// O andamento da varredura: pastas olhadas, jogos achados até agora e a pasta atual. <see cref="Found"/> é o jogo que esta pasta acabou de revelar (nulo nas pastas que não são jogo);
/// <see cref="EstimatedDirectories"/> é a conta prévia de pastas, quando pedida; ela é uma estimativa, e a varredura pode olhar mais ou menos pastas que isso.
/// </summary>
public sealed record LibraryScanProgress(int Directories, int Games, string Current)
{
    public LibraryGame? Found { get; init; }

    public int? EstimatedDirectories { get; init; }
}

/// <summary>O que a varredura achou. <paramref name="Cancelled"/> diz que parou no meio: <paramref name="Games"/> é o que deu tempo de achar.</summary>
public sealed record LibraryScanResult(IReadOnlyList<LibraryGame> Games, int Directories, bool Cancelled);

/// <summary>
/// Varre uma pasta ou unidade atrás de jogos. Uma pasta onde uma engine é reconhecida é um jogo e não se desce dentro dela; as outras são percorridas até
/// <see cref="LibraryScanOptions.MaxDepth"/>. Pula pastas de sistema, atalhos de pasta (para não girar em laço) e o que o sistema não deixa ler.
/// Só lê nomes de arquivo; não abre nem altera nada.
/// </summary>
public sealed class LibraryScanner(EngineRecognizer recognizer, IGameWindowTitleReader? titles = null, TimeProvider? time = null)
{
    private static readonly HashSet<string> SystemFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "ProgramData", "System Volume Information", "Recovery", "AppData", "node_modules",
        "WindowsApps", "WinSxS", "Documents and Settings", "PerfLogs", "MSOCache", "Config.Msi", "Boot",
    };

    private static readonly EnumerationOptions Listing = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
    };

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Um nome de pasta que a varredura nunca percorre (sistema, lixeira, pastas ocultas por convenção).</summary>
    public static bool IsSystemFolder(string name) =>
        SystemFolders.Contains(name) || name.StartsWith('$') || (name.StartsWith('.') && name.Length > 1);

    /// <exception cref="LibraryException">A pasta não existe.</exception>
    public LibraryScanResult Scan(string root, LibraryScanOptions? options = null, IProgress<LibraryScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        options ??= new LibraryScanOptions();
        var top = Path.GetFullPath(root);
        if (!Directory.Exists(top))
        {
            throw new LibraryException($"A pasta não existe: {top}");
        }

        var games = new List<LibraryGame>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((top, 0));
        var estimate = options.EstimateFolders ? EstimateFolders(top, options.MaxDepth, cancellationToken) : (int?)null;
        var directories = 0;
        var cancelled = false;
        while (pending.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var (path, depth) = pending.Pop();
            directories++;
            var game = Inspect(path, options);
            if (game is not null)
            {
                games.Add(game);
            }

            progress?.Report(new LibraryScanProgress(directories, games.Count, path) { Found = game, EstimatedDirectories = estimate is { } total ? Math.Max(total, directories) : null });
            if (game is null && depth < options.MaxDepth)
            {
                foreach (var child in Children(path))
                {
                    pending.Push((child, depth + 1));
                }
            }
        }

        return new LibraryScanResult([.. games.OrderBy(g => g.Folder, StringComparer.OrdinalIgnoreCase)], directories, cancelled);
    }

    /// <summary>Quantas pastas a varredura deve olhar, contadas só pelo nome: não desce dentro de pasta que tem executável, que é quase sempre a de um jogo.</summary>
    private static int EstimateFolders(string root, int maxDepth, CancellationToken cancellationToken)
    {
        var count = 0;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var (path, depth) = pending.Pop();
            count++;
            if (depth >= maxDepth || HasExecutable(path))
            {
                continue;
            }

            foreach (var child in Children(path))
            {
                pending.Push((child, depth + 1));
            }
        }

        return count;
    }

    private static bool HasExecutable(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.exe", Listing).Any();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private LibraryGame? Inspect(string folder, LibraryScanOptions options)
    {
        var engine = recognizer.Recognize(folder);
        if (engine is null && !options.IncludeUnknown)
        {
            return null;
        }

        var exe = GameExecutables.Pick(folder);
        if (engine is null)
        {
            if (exe is null)
            {
                return null;
            }

            engine = LibraryEngine.Unknown;
        }

        var windowTitle = titles?.Read(folder, exe);
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        var title = GameNames.CleanFolderName(folder) ?? windowTitle ?? (string.IsNullOrWhiteSpace(name) ? folder : name);
        return new LibraryGame(GameLibrary.IdFor(folder), GameLibrary.NormalizeFolder(folder), exe, engine, title, windowTitle, _time.GetUtcNow());
    }

    /// <summary>As subpastas, em ordem alfabética invertida: a pilha as tira em ordem alfabética.</summary>
    private static IEnumerable<string> Children(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder, "*", Listing)
                .Where(d => !IsSystemFolder(Path.GetFileName(d)))
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>Escolhe o executável do jogo entre os da pasta, deixando de fora desinstaladores, instaladores e ajudantes.</summary>
public static class GameExecutables
{
    private static readonly string[] HelperPrefixes = ["unins", "uninst", "vcredist", "vc_redist", "dxsetup", "dxwebsetup", "dotnet", "crashhandler", "unitycrashhandler", "crashpad", "notification_helper", "createdump", "setup", "install"];

    private static readonly HashSet<string> HelperNames = new(StringComparer.OrdinalIgnoreCase) { "config", "configtool", "settings", "updater", "update", "patch" };

    /// <summary>O executável (nome relativo à pasta) ou nulo se não há. Com vários, vale o que tem o nome da pasta e, na falta dele, o maior.</summary>
    public static string? Pick(string folder)
    {
        List<FileInfo> candidates;
        try
        {
            candidates = [.. new DirectoryInfo(folder).EnumerateFiles("*.exe", SearchOption.TopDirectoryOnly).Where(f => !IsHelper(f.Name))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        switch (candidates.Count)
        {
            case 0:
                return null;
            case 1:
                return candidates[0].Name;
        }

        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        var sameName = candidates.FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f.Name), folderName, StringComparison.OrdinalIgnoreCase));
        return (sameName ?? candidates.OrderByDescending(f => f.Length).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).First()).Name;
    }

    private static bool IsHelper(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return HelperNames.Contains(stem) || HelperPrefixes.Any(p => stem.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
