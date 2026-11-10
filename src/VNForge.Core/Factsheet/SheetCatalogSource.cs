using VNForge.Core.Library;
using VNForge.Core.Text;

namespace VNForge.Core.Factsheet;

/// <summary>
/// A ficha em português (d9, #554) como o catálogo a lê: <see cref="SheetStore"/> dá a sinopse, os personagens e os nomes de tag e de traço traduzidos. Entrega o texto cru, com a
/// marca de spoiler e a marca sexual de cada traço; quem esconde o quê é o <see cref="LibraryCatalog"/>, pela privacidade. Jogo sem ficha buscada dá <c>null</c>, e o catálogo usa o
/// texto original do VNDB. Arquivo de ficha ilegível não vira ficha vazia: sobe a <see cref="LibraryException"/> do <see cref="SheetStore"/>, com o arquivo na mensagem.
/// </summary>
public sealed class SheetCatalogSource(SheetStore store) : ILibrarySheetSource
{
    private SheetTerms? _terms;

    /// <exception cref="LibraryException">Um arquivo da ficha existe e não pôde ser lido.</exception>
    public LibrarySheet? SheetOf(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.VndbId is not { } id || store.LoadGame(id) is not { } sheet)
        {
            return null;
        }

        var terms = _terms ??= store.LoadTerms();
        var characters = store.LoadCharacters(sheet.CharacterIds).Select(c => Character(c, id, terms)).ToList();
        var tagNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in sheet.Tags)
        {
            if (terms.Find(SheetTermKind.Tag, tag.Id) is { Text.HasTarget: true } term)
            {
                tagNames[tag.Id] = term.Text.Target!;
            }
        }

        return new LibrarySheet(sheet.Synopsis?.Shown, sheet.Synopsis?.HasTarget == true, characters, tagNames, sheet.Synopsis is { HasTarget: true, Status: >= TranslationStatus.Reviewed });
    }

    private static LibrarySheetCharacter Character(CharacterSheet character, string visualNovelId, SheetTerms terms)
    {
        var appearance = character.In(visualNovelId);
        var traits = character.Traits.Select(t => new LibrarySheetTrait(terms.Shown(SheetTermKind.Trait, t.Id, t.Name), t.Spoiler, t.Lie, t.Sexual)).ToList();
        return new LibrarySheetCharacter(
            character.Name,
            character.Original,
            SheetLabels.Role(appearance?.Role ?? "appears"),
            character.Description?.Shown,
            character.Description?.HasTarget == true,
            traits,
            character.Image,
            appearance?.Spoiler ?? 0);
    }
}
