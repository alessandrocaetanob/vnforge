using VNForge.Core.Library;

namespace VNForge.Core.Factsheet;

/// <summary>
/// Tudo o que a aba Ficha lê de uma vez: a ficha do jogo e dos personagens, os nomes traduzidos de tags e traços, a privacidade da Biblioteca (de onde sai o nível de spoiler
/// inicial) e as tags do usuário no jogo, quando ele está na Biblioteca.
/// </summary>
public sealed record SheetPageData(GameSheet Game, IReadOnlyList<CharacterSheet> Characters, SheetTerms Terms, LibraryPrivacy Privacy, IReadOnlyList<string> UserTags);

/// <summary>Lê os arquivos da ficha e da Biblioteca para a tela. Os arquivos da Biblioteca que faltam ou não abrem não impedem a ficha: valem a privacidade padrão e nenhuma tag sua.</summary>
public static class SheetPage
{
    /// <summary>Os dados da ficha do jogo do VNDB, ou nulo quando ela ainda não foi buscada.</summary>
    /// <exception cref="LibraryException">Um arquivo da ficha existe e está ilegível: a leitura recusa em vez de mostrar uma ficha vazia.</exception>
    public static SheetPageData? Load(string configDirectory, string visualNovelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(visualNovelId);
        var store = new SheetStore(configDirectory);
        var game = store.LoadGame(visualNovelId);
        if (game is null)
        {
            return null;
        }

        var privacy = new LibraryPrivacy();
        IReadOnlyList<string> userTags = [];
        try
        {
            var library = new LibraryFile(configDirectory).Load();
            privacy = library.Privacy;
            if (library.Games.FirstOrDefault(g => string.Equals(g.VndbId, visualNovelId, StringComparison.Ordinal)) is { } held)
            {
                userTags = new UserTagsFile(configDirectory).Load().TagsOf(held.Id);
            }
        }
        catch (LibraryException)
        {
            // Sem a Biblioteca legível a ficha abre com o que esconde por padrão; quem a conserta é a Biblioteca.
        }

        return new SheetPageData(game, store.LoadCharacters(game.CharacterIds), store.LoadTerms(), privacy, userTags);
    }
}
