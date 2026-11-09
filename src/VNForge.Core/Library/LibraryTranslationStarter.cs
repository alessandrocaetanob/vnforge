using VNForge.Core.Engines;
using VNForge.Core.Memory;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.Core.Library;

/// <summary>O projeto que "Traduzir este jogo" criou: o jogo da biblioteca já ligado a ele, o que o <see cref="ProjectInitializer"/> montou e a ficha do VNDB, se o jogo tinha casamento confirmado.</summary>
public sealed record TranslationStartResult(LibraryGame Game, ProjectInitResult Project, VndbLink? Vndb);

/// <summary>
/// "Traduzir este jogo": cria o projeto de tradução de um jogo da biblioteca já preenchido com o que a biblioteca sabe (pasta, executável, título e, quando o jogo
/// tem casamento confirmado, a ficha e os personagens do VNDB, como em <c>vnf init --vndb</c>) e liga o jogo ao projeto.
/// </summary>
public static class LibraryTranslationStarter
{
    private const int MaxFolderName = 80;

    /// <summary>
    /// Cria o projeto em <paramref name="projectDir"/> e liga <paramref name="game"/> a ele na <paramref name="library"/>. O VNDB é consultado antes de criar qualquer coisa:
    /// se ele falha, não fica projeto pela metade. O que <paramref name="options"/> traz vale mais que o que a biblioteca sabe do jogo.
    /// </summary>
    /// <exception cref="LibraryException">O jogo já tem projeto ligado ou a engine dele ainda não é traduzida pelo app.</exception>
    /// <exception cref="VndbLinkException">O VNDB não tem o jogo confirmado.</exception>
    /// <exception cref="VndbException">O VNDB não respondeu.</exception>
    /// <exception cref="ProjectInitException">A pasta do jogo ou do projeto não serve, ou o executável não foi resolvido.</exception>
    public static async Task<TranslationStartResult> StartAsync(
        GameLibrary library,
        LibraryGame game,
        string projectDir,
        IReadOnlyList<IEngine> engines,
        IVndbCatalog catalog,
        ProjectInitOptions? options = null,
        PersonalGlossary? personalGlossary = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(catalog);
        if (game.ProjectPath is { } existing)
        {
            throw new LibraryException(
                $"{game.Title} já tem o projeto {existing}. Para criar outro, desligue este antes com vnf library link {game.Id} --remove.");
        }

        if (!game.Engine.Translatable)
        {
            throw new LibraryException($"O VNForge ainda não traduz jogos {game.Engine.Name}: {game.Title} fica na biblioteca, mas não vira projeto.");
        }

        var link = game.VndbId is { } vndbId ? await VndbProjectLinker.BuildAsync(catalog, vndbId, cancellationToken).ConfigureAwait(false) : null;
        options ??= new ProjectInitOptions();
        var project = ProjectInitializer.Initialize(
            projectDir,
            game.Folder,
            engines,
            options with { Name = options.Name ?? game.Title, Exe = options.Exe ?? game.Exe, Vndb = link },
            personalGlossary);

        var linked = LibraryProjectLinker.Link(library, game, project.Folder.Root);
        return new TranslationStartResult(linked, project, link);
    }

    /// <summary>
    /// O nome de pasta que o projeto leva quando a pessoa não escolhe: o título do jogo sem os caracteres que o sistema de arquivos não aceita (viram espaço, e
    /// espaços repetidos viram um), até 80 caracteres. Título que não sobra nada vira o id do jogo.
    /// </summary>
    public static string SuggestedFolderName(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = string.Concat(game.Title.Select(c => invalid.Contains(c) ? ' ' : c));
        var name = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        name = name.Length > MaxFolderName ? name[..MaxFolderName] : name;
        name = name.TrimEnd(' ', '.');
        return name.Length == 0 ? game.Id : name;
    }
}
