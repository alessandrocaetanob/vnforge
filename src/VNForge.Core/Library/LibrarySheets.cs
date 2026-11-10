using VNForge.Core.Vndb;

namespace VNForge.Core.Library;

/// <summary>
/// De onde o catálogo (<see cref="LibraryCatalog"/>) lê a ficha em português de um jogo: sinopse, personagens e nomes de tags. É a fronteira pequena que a ficha traduzida
/// (d9, #554) preenche; sem ela (ou sem ficha para o jogo) o catálogo usa o texto original do VNDB que a Biblioteca já guarda.
/// </summary>
public interface ILibrarySheetSource
{
    /// <summary>A ficha do jogo, ou nulo se não há ficha dele.</summary>
    LibrarySheet? SheetOf(LibraryGame game);
}

/// <summary>
/// A ficha de um jogo. <paramref name="Synopsis"/> vem no formato do VNDB (pode ter <c>[spoiler]</c>); nula, o catálogo usa a sinopse original de <see cref="LibraryVndbInfo"/>.
/// <paramref name="SynopsisTranslated"/> diz se o texto está em português. <paramref name="TagNames"/> dá o nome em português de cada tag do VNDB, pelo id (<see cref="LibraryTag.Id"/>);
/// tag que não está nela sai com o nome do VNDB.
/// </summary>
public sealed record LibrarySheet(
    string? Synopsis,
    bool SynopsisTranslated,
    IReadOnlyList<LibrarySheetCharacter> Characters,
    IReadOnlyDictionary<string, string> TagNames);

/// <summary>
/// Um personagem da ficha. <paramref name="Spoiler"/> é o nível do VNDB (0, 1 ou 2): com a privacidade escondendo spoiler, o catálogo omite o personagem.
/// <paramref name="Translated"/> diz se <paramref name="Description"/> e <paramref name="Role"/> estão em português.
/// </summary>
public sealed record LibrarySheetCharacter(
    string Name,
    string? OriginalName,
    string? Role,
    string? Description,
    bool Translated,
    IReadOnlyList<LibrarySheetTrait> Traits,
    VndbImageRef? Image,
    int Spoiler);

/// <summary>
/// Um traço do personagem. <paramref name="Spoiler"/> é o nível do VNDB (0, 1 ou 2); <paramref name="Lie"/> marca o traço que o VNDB diz ser mentira da história; <paramref name="Sexual"/> é
/// a marca sexual do VNDB, nula quando a ficha foi gravada sem o campo. O catálogo aplica a regra da ficha (<see cref="Factsheet.SheetViewer.IsTraitHidden(int, bool, bool?, LibraryPrivacy)"/>).
/// </summary>
public sealed record LibrarySheetTrait(string Name, int Spoiler, bool Lie, bool? Sexual);
