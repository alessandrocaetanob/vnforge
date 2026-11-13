using VNForge.Core.Projects;

namespace VNForge.Core.Library;

/// <summary>Um projeto de tradução e o jogo da biblioteca a que ele pertence.</summary>
public sealed record LibraryProjectLink(string ProjectPath, LibraryGame Game);

/// <summary>
/// Liga os projetos de tradução aos jogos da biblioteca. O projeto aponta para a pasta do jogo no <c>vnforge.json</c> (<c>game.path</c>); o jogo da biblioteca com essa pasta
/// ganha o <see cref="LibraryGame.ProjectPath"/>. Só lê os <c>vnforge.json</c>.
/// </summary>
public static class LibraryProjectLinker
{
    /// <summary>
    /// Procura, entre <paramref name="projectFolders"/>, os projetos cujo jogo está na biblioteca e liga cada um. Pasta que não é projeto, ou projeto de jogo
    /// que a biblioteca não tem, é ignorada. Devolve só as ligações novas ou trocadas.
    /// </summary>
    public static IReadOnlyList<LibraryProjectLink> LinkAll(GameLibrary library, IEnumerable<string> projectFolders)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(projectFolders);
        var links = new List<LibraryProjectLink>();
        foreach (var folder in projectFolders)
        {
            var gamePath = TryReadGameFolder(folder, out var title);
            if (gamePath is null)
            {
                continue;
            }

            var game = library.Games.FirstOrDefault(g => GameLibrary.IsUnder(g.Folder, gamePath) && GameLibrary.IsUnder(gamePath, g.Folder));
            if (game is null)
            {
                continue;
            }

            var projectPath = GameLibrary.NormalizeFolder(folder);
            var linked = game with { ProjectPath = projectPath, WindowTitle = game.WindowTitle ?? title };
            if (linked != game)
            {
                library.Replace(linked);
                links.Add(new LibraryProjectLink(projectPath, linked));
            }
        }

        return links;
    }

    /// <summary>Liga o projeto em <paramref name="projectFolder"/> ao jogo, mesmo que o <c>game.path</c> dele aponte para outro lugar (o jogo foi movido).</summary>
    /// <exception cref="LibraryException">A pasta não é um projeto do VNForge.</exception>
    public static LibraryGame Link(GameLibrary library, LibraryGame game, string projectFolder)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(game);
        var linked = game with { ProjectPath = GameLibrary.NormalizeFolder(RequireProject(projectFolder)) };
        library.Replace(linked);
        return linked;
    }

    private static string RequireProject(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        var root = Path.GetFullPath(projectFolder);
        if (!File.Exists(Path.Combine(root, ProjectFolder.ConfigFileName)))
        {
            throw new LibraryException($"A pasta '{root}' não é um projeto do VNForge (falta o {ProjectFolder.ConfigFileName}).");
        }

        return root;
    }

    /// <summary>
    /// Como <see cref="Link"/>, mas quando o <c>game.path</c> do projeto não é a pasta do jogo da biblioteca (o jogo foi movido) o projeto passa a apontar para a pasta da biblioteca,
    /// pelo <see cref="ProjectGameRelocator"/>. Se o jogo de lá não é o do projeto, a exceção sobe antes de qualquer gravação no projeto e a biblioteca não é alterada.
    /// </summary>
    /// <returns>O jogo ligado e a troca de pasta feita; nula quando o projeto já apontava para a pasta do jogo ou o <c>vnforge.json</c> não pode ser lido (nada a trocar).</returns>
    /// <exception cref="LibraryException">A pasta não é um projeto do VNForge.</exception>
    /// <exception cref="VNForgeException">A pasta do jogo da biblioteca não existe ou não é o mesmo jogo do projeto.</exception>
    public static (LibraryGame Game, GameRelocation? Relocation) LinkFollowingGame(GameLibrary library, LibraryGame game, string projectFolder)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(game);
        var root = RequireProject(projectFolder);
        var configured = TryReadGameFolder(root, out _);
        var moved = configured is null || (GameLibrary.IsUnder(configured, game.Folder) && GameLibrary.IsUnder(game.Folder, configured))
            ? null
            : ProjectGameRelocator.Move(ProjectFolder.Open(root), game.Folder);
        return (Link(library, game, root), moved);
    }

    /// <summary>Tira a ligação do projeto. O projeto em si não é tocado.</summary>
    public static LibraryGame Unlink(GameLibrary library, LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(game);
        var unlinked = game with { ProjectPath = null };
        library.Replace(unlinked);
        return unlinked;
    }

    private static string? TryReadGameFolder(string projectFolder, out string? windowTitle)
    {
        windowTitle = null;
        try
        {
            var project = ProjectFolder.Open(projectFolder);
            var config = project.LoadConfig();
            windowTitle = string.IsNullOrWhiteSpace(config.Game.Title?.Original) ? null : config.Game.Title.Original.Trim();
            return Path.GetFullPath(config.Game.Path, project.Root);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
