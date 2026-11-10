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
public sealed record SheetAppearance(string VisualNovelId, string Role, int Spoiler);

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
    DateTimeOffset FetchedAt,
    string? Released = null,
    string? OriginalLanguage = null,
    double? Rating = null);

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

    /// <summary>O papel como o selo do cartão o escreve: "Protagonista", "Principal", "Secundário" (ou "Secundária" para uma personagem feminina) e "Aparece".</summary>
    public static string RoleBadge(string role, string? genderLabel) => role switch
    {
        "main" => "Protagonista",
        "primary" => "Principal",
        "side" => string.Equals(genderLabel, "feminino", StringComparison.Ordinal) ? "Secundária" : "Secundário",
        "appears" => "Aparece",
        _ => role,
    };

    /// <summary>O selo da etapa como os cartões e os botões o escrevem: "Traduzido pela IA", "Traduzido", "Revisado", "Aprovado" e "Sem tradução"; <paramref name="feminine"/> concorda com um substantivo feminino (a sinopse).</summary>
    public static string StatusBadge(Text.TranslationStatus status, bool feminine = false) => status switch
    {
        Text.TranslationStatus.Machine => feminine ? "Traduzida pela IA" : "Traduzido pela IA",
        Text.TranslationStatus.Translated => feminine ? "Traduzida" : "Traduzido",
        Text.TranslationStatus.Reviewed => feminine ? "Revisada" : "Revisado",
        Text.TranslationStatus.Approved => feminine ? "Aprovada" : "Aprovado",
        _ => "Sem tradução",
    };

    /// <summary>A duração como a ficha a escreve: "Média · 18 h (47 votos)"; só a categoria traz a faixa em horas ("Média · 10 a 30 h"), e sem votos vale "Sem votos".</summary>
    public static string LengthDetail(int? minutes, int? category, int votes)
    {
        (string Name, string Range)? band = category switch
        {
            1 => ("Muito curta", "menos de 2 h"),
            2 => ("Curta", "2 a 10 h"),
            3 => ("Média", "10 a 30 h"),
            4 => ("Longa", "30 a 50 h"),
            5 => ("Muito longa", "mais de 50 h"),
            _ => null,
        };
        if (minutes is null && band is null)
        {
            return "Sem votos";
        }

        var parts = new List<string>();
        if (band is { } known)
        {
            parts.Add(known.Name);
        }

        if (minutes is { } m)
        {
            parts.Add(m >= 60 ? $"{(int)Math.Round(m / 60.0, MidpointRounding.AwayFromZero)} h" : $"{m} min");
        }
        else if (band is { } only)
        {
            parts.Add(only.Range);
        }

        var text = string.Join(" · ", parts);
        return votes > 0 ? $"{text} ({votes} {(votes == 1 ? "voto" : "votos")})" : text;
    }

    /// <summary>"2019, em japonês": o ano do lançamento do VNDB (<c>2019-05-24</c>, <c>2019-05</c> ou <c>2019</c>) e a língua original; nulo quando o VNDB não diz nenhum dos dois.</summary>
    public static string? Released(string? released, string? language)
    {
        var year = released is { Length: >= 4 } text && text[..4].All(char.IsAsciiDigit) ? text[..4] : string.Equals(released, "tba", StringComparison.OrdinalIgnoreCase) ? "A anunciar" : null;
        var name = LanguageName(language);
        return (year, name) switch
        {
            (null, null) => null,
            (null, _) => $"em {name}",
            (_, null) => year,
            _ => $"{year}, em {name}",
        };
    }

    /// <summary>O nome em português de uma língua do VNDB (<c>ja</c>, <c>en</c>...); nulo para uma que a ficha não conhece.</summary>
    public static string? LanguageName(string? code) => code?.ToLowerInvariant() switch
    {
        "ja" => "japonês",
        "en" => "inglês",
        "zh" or "zh-hans" or "zh-hant" => "chinês",
        "ko" => "coreano",
        "pt" or "pt-br" => "português",
        "es" => "espanhol",
        "fr" => "francês",
        "de" => "alemão",
        "ru" => "russo",
        "it" => "italiano",
        _ => null,
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
