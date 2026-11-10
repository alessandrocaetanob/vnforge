using System.Globalization;
using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.Core.Factsheet;

/// <summary>
/// Onde um personagem aparece. Por jogo, o VNDB dá o papel e o nível de spoiler do personagem; quando o mesmo jogo vem de várias edições, vale o maior papel e o menor
/// spoiler. É o que a página própria dos personagens (#558) lê para o "Aparece em".
/// </summary>
/// <param name="Role"><c>main</c> (protagonista), <c>primary</c> (principal), <c>side</c> (secundário) ou <c>appears</c> (aparece).</param>
/// <param name="Spoiler">0 (nenhum), 1 (pequeno) ou 2 (grande): o quanto saber que o personagem existe neste jogo conta da história.</param>
/// <param name="Editions">Papel e spoiler de cada edição que o VNDB lista para o personagem neste jogo; nulo em ficha gravada antes de a Biblioteca guardá-los. A Biblioteca de personagens (<see cref="LibraryCharacters"/>) escolhe entre eles pela edição casada com a pasta.</param>
public sealed record SheetAppearance(string VisualNovelId, string Role, int Spoiler, IReadOnlyList<SheetAppearanceEdition>? Editions = null);

/// <summary>O papel e o spoiler do personagem numa edição do jogo. <paramref name="ReleaseId"/> nulo vale para toda edição que o VNDB não cita à parte.</summary>
public sealed record SheetAppearanceEdition(string? ReleaseId, string Role, int Spoiler);

/// <summary>
/// Um personagem do VNDB com a descrição traduzida. Não depende de um jogo: o mesmo personagem em dois jogos é um registro só (<see cref="Appearances"/> lista os jogos),
/// e traduzir a descrição uma vez vale para os dois. Os nomes dos traços se traduzem à parte, em <see cref="SheetTerms"/>, porque valem para todos os personagens.
/// <paramref name="Gender"/> traz o gênero que aparece sem spoiler e o real.
/// </summary>
public sealed record CharacterSheet(
    string Id,
    string Name,
    string? Original,
    IReadOnlyList<string> Aliases,
    VndbGender? Gender,
    VndbImageRef? Image,
    SheetText? Description,
    IReadOnlyList<VndbTrait> Traits,
    IReadOnlyList<SheetAppearance> Appearances)
{
    /// <summary>A aparição no jogo, ou nula quando o personagem não está nele.</summary>
    public SheetAppearance? In(string visualNovelId) => Appearances.FirstOrDefault(a => a.VisualNovelId == visualNovelId);
}

/// <summary>
/// A ficha de um jogo do VNDB: sinopse traduzida, duração, as tags (cujos nomes se traduzem em <see cref="SheetTerms"/>) e os ids dos personagens, que ficam à parte
/// (<see cref="CharacterSheet"/>). É do jogo do VNDB, não do item da biblioteca: dois itens do mesmo jogo dividem a ficha.
/// </summary>
public sealed record GameSheet(
    string VisualNovelId,
    string Title,
    string? OriginalTitle,
    SheetText? Synopsis,
    int? Length,
    int? LengthMinutes,
    int LengthVotes,
    IReadOnlyList<LibraryTag> Tags,
    IReadOnlyList<string> CharacterIds,
    bool CharactersTruncated,
    DateTimeOffset FetchedAt);

/// <summary>Os rótulos em português do que o VNDB marca nos personagens e no jogo.</summary>
public static class SheetLabels
{
    private static readonly string[] RoleOrder = ["main", "primary", "side", "appears"];

    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>A ordem do papel, do protagonista para baixo; papel desconhecido vai por último.</summary>
    public static int RoleRank(string role) => Array.IndexOf(RoleOrder, role) is var i and >= 0 ? i : RoleOrder.Length;

    public static string Role(string role) => role switch
    {
        "main" => "protagonista",
        "primary" => "principal",
        "side" => "secundário",
        "appears" => "participação",
        _ => role,
    };

    /// <summary>A etapa de um texto da ficha, como a CLI e a tela a escrevem.</summary>
    public static string Status(Text.TranslationStatus status) => status switch
    {
        Text.TranslationStatus.Machine => "traduzido pela IA",
        Text.TranslationStatus.Translated => "traduzido à mão",
        Text.TranslationStatus.Reviewed => "revisado",
        Text.TranslationStatus.Approved => "aprovado",
        _ => "sem tradução",
    };

    /// <summary>Masculino, feminino, não binário ou ambíguo; <c>desconhecido</c> quando o VNDB não diz.</summary>
    public static string Gender(string? gender) => gender switch
    {
        "m" => "masculino",
        "f" => "feminino",
        "o" => "não binário",
        "a" => "ambíguo",
        _ => "desconhecido",
    };

    /// <summary>A duração: pelo tempo médio dos votos quando há, senão pela categoria de 1 a 5 do VNDB.</summary>
    public static string Length(int? minutes, int? category)
    {
        if (minutes is { } m)
        {
            return m >= 60 ? $"cerca de {(m / 60.0).ToString("0", PortugueseBrazil)} h" : $"{m} min";
        }

        return category switch
        {
            1 => "muito curta (menos de 2 h)",
            2 => "curta (2 a 10 h)",
            3 => "média (10 a 30 h)",
            4 => "longa (30 a 50 h)",
            5 => "muito longa (mais de 50 h)",
            _ => "sem votos",
        };
    }
}
