namespace VNForge.Core.Vndb;

/// <summary>Uma imagem do VNDB (capa ou retrato). <paramref name="Sexual"/> e <paramref name="Violence"/> são a média dos votos de 0 a 2, o que a Biblioteca usa para esconder conteúdo adulto.</summary>
public sealed record VndbImage(string Url, string? ThumbnailUrl, int? Width, int? Height, double Sexual, double Violence);

/// <summary>Uma tag aplicada a um jogo. <paramref name="Rating"/> vai de 0 (exclusivo) a 3; <paramref name="Spoiler"/> é 0 (nenhum), 1 (pequeno) ou 2 (grande).</summary>
public sealed record VndbTag(string Id, string Name, string Category, double Rating, int Spoiler, bool Lie);

/// <summary>Um título do jogo em um idioma. <paramref name="Title"/> está na escrita original e <paramref name="Latin"/> é a romanização, quando há.</summary>
public sealed record VndbTitle(string Language, string Title, string? Latin, bool Official, bool Main);

/// <summary>
/// Um jogo do VNDB. <paramref name="Description"/> vem com os códigos de formatação do VNDB (<c>[b]</c>, <c>[url=...]</c>, <c>[spoiler]</c>), sem converter.
/// <paramref name="Length"/> vai de 1 (muito curto) a 5 (muito longo) e só vale quando <paramref name="LengthMinutes"/> é nulo.
/// <paramref name="Rating"/> é a nota bayesiana do VNDB, de 10 a 100 (campo <c>rating</c> da API), ou nula quando ninguém votou; <see cref="VndbRating.Label"/> a escreve como "nota 7,9".
/// </summary>
public sealed record VndbVisualNovel(
    string Id,
    string Title,
    string? AltTitle,
    IReadOnlyList<VndbTitle> Titles,
    IReadOnlyList<string> Aliases,
    string? OriginalLanguage,
    int? DevStatus,
    string? Released,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Platforms,
    VndbImage? Image,
    int? Length,
    int? LengthMinutes,
    int LengthVotes,
    string? Description,
    IReadOnlyList<VndbTag> Tags,
    double? Rating = null);

/// <summary>O gênero de um personagem: o que aparece sem spoiler e o real. Os valores são <c>m</c>, <c>f</c>, <c>o</c> (não binário), <c>a</c> (ambíguo) ou nulo.</summary>
public sealed record VndbGender(string? Visible, string? Spoiler);

/// <summary>Um traço do personagem. <paramref name="GroupName"/> (Cabelo, Corpo, Papel) acompanha o nome, porque o traço sozinho não se explica. <paramref name="Sexual"/> é o campo <c>sexual</c> que o VNDB marca no traço; nulo é "não sei" (ficha gravada antes de o campo existir) e a Biblioteca trata como sexual até a próxima busca.</summary>
public sealed record VndbTrait(string Id, string Name, string GroupId, string GroupName, int Spoiler, bool Lie, bool? Sexual = null);

/// <summary>
/// Onde o personagem aparece. O mesmo jogo pode vir mais de uma vez, com <paramref name="ReleaseId"/> diferente, porque papel e spoiler variam por edição.
/// <paramref name="Role"/> é <c>main</c> (protagonista), <c>primary</c>, <c>side</c> ou <c>appears</c>.
/// </summary>
public sealed record VndbCharacterAppearance(string VisualNovelId, string Role, int Spoiler, string? ReleaseId);

/// <summary>Um personagem do VNDB, com os jogos em que aparece e os traços.</summary>
public sealed record VndbCharacter(
    string Id,
    string Name,
    string? Original,
    IReadOnlyList<string> Aliases,
    string? Description,
    VndbImage? Image,
    VndbGender? Gender,
    IReadOnlyList<VndbCharacterAppearance> Appearances,
    IReadOnlyList<VndbTrait> Traits);

/// <summary>Um idioma de uma edição. <paramref name="MachineTranslated"/> marca tradução automática, que não conta como tradução de verdade.</summary>
public sealed record VndbReleaseLanguage(string Language, string? Title, string? Latin, bool MachineTranslated, bool Main);

/// <summary>A ligação de uma edição com um jogo. <paramref name="ReleaseType"/> é <c>complete</c>, <c>partial</c> ou <c>trial</c>.</summary>
public sealed record VndbReleaseVisualNovel(string VisualNovelId, string ReleaseType);

/// <summary>Uma edição (release) de um jogo: idiomas, plataformas, engine, se é um patch de tradução e a classificação etária.</summary>
public sealed record VndbRelease(
    string Id,
    string Title,
    string? AltTitle,
    IReadOnlyList<VndbReleaseLanguage> Languages,
    IReadOnlyList<string> Platforms,
    string? Released,
    int? MinAge,
    bool Patch,
    bool Freeware,
    bool Official,
    string? Engine,
    int? Voiced,
    IReadOnlyList<VndbReleaseVisualNovel> VisualNovels);

/// <summary>Quem é dono do token e o que ele permite (<c>listread</c>, <c>listwrite</c>).</summary>
public sealed record VndbAuthInfo(string UserId, string Username, IReadOnlyList<string> Permissions);

/// <summary>Um rótulo da lista do usuário (Playing, Finished, Stalled, Dropped, Wishlist, Blacklist, Voted ou um rótulo próprio).</summary>
public sealed record VndbListLabel(int Id, string Label);

/// <summary>Um jogo na lista do usuário. <paramref name="Vote"/> vai de 10 a 100.</summary>
public sealed record VndbListEntry(
    string VisualNovelId,
    string? Title,
    DateTimeOffset Added,
    DateTimeOffset LastModified,
    DateTimeOffset? Voted,
    int? Vote,
    DateOnly? Started,
    DateOnly? Finished,
    string? Notes,
    IReadOnlyList<VndbListLabel> Labels);
