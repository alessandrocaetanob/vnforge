using System.Text.Json.Serialization;

namespace VNForge.Core.Library;

/// <summary>De onde veio o reconhecimento da engine de um jogo da biblioteca.</summary>
public enum LibraryEngineSource
{
    /// <summary>Uma engine registrada, pelo <c>IEngine.Detect</c>: o app sabe traduzir o jogo.</summary>
    Engine,

    /// <summary>Uma assinatura de arquivo (<c>.xp3</c>, <c>Scene.pck</c>, <c>.rpa</c>...) de uma engine que o app ainda não traduz.</summary>
    Signature,

    /// <summary>Pasta com executável e nenhuma assinatura conhecida; só entra na varredura quando a pessoa pede.</summary>
    Unknown,
}

/// <summary>Quem confirmou o jogo do VNDB de um item da biblioteca.</summary>
public enum LibraryConfirmation
{
    /// <summary>Ainda não há jogo do VNDB confirmado.</summary>
    None,

    /// <summary>O casamento confirmou sozinho: pontuação alta e sem empate com o segundo colocado.</summary>
    Auto,

    /// <summary>A pessoa confirmou.</summary>
    Manual,
}

/// <summary>A engine reconhecida na pasta. <paramref name="Id"/> é o <c>IEngine.Id</c> ou o da assinatura (<c>kirikiri</c>, <c>siglus</c>, <c>renpy</c>...); <c>unknown</c> quando não reconhece.</summary>
public sealed record LibraryEngine(string Id, string Name, LibraryEngineSource Source, string? Summary = null)
{
    public const string UnknownId = "unknown";

    public static LibraryEngine Unknown { get; } = new(UnknownId, "Desconhecida", LibraryEngineSource.Unknown);

    /// <summary>O app traduz jogos desta engine (ela está registrada e reconheceu a pasta).</summary>
    [JsonIgnore]
    public bool Translatable => Source == LibraryEngineSource.Engine;
}

/// <summary>Um jogo do VNDB que pode ser o da pasta. <paramref name="Score"/> vai de 0 a 1; <paramref name="Evidence"/> diz de onde veio o casamento (<c>janela</c>, <c>pasta</c>, <c>exe</c>).</summary>
public sealed record LibraryCandidate(string VndbId, string Title, string? OriginalTitle, string? Released, double Score, IReadOnlyList<string> Evidence);

/// <summary>Uma tag do VNDB no jogo. <paramref name="Spoiler"/> é 0 (nenhum), 1 (pequeno) ou 2 (grande).</summary>
public sealed record LibraryTag(string Id, string Name, string Category, double Rating, int Spoiler);

/// <summary>
/// O que a Biblioteca mostra do jogo do VNDB: capa, sinopse, duração e tags. A sinopse vem com os códigos do VNDB (<c>[spoiler]</c>) sem converter;
/// quem mostra passa por <see cref="LibraryVisibility"/>. <paramref name="Adult"/> marca capa ou tags de conteúdo adulto.
/// </summary>
public sealed record LibraryVndbInfo(
    string Title,
    string? OriginalTitle,
    string? Released,
    string? CoverUrl,
    string? Synopsis,
    int? Length,
    int? LengthMinutes,
    int LengthVotes,
    IReadOnlyList<LibraryTag> Tags,
    bool Adult);

/// <summary>
/// Um jogo da biblioteca. <paramref name="Id"/> é estável (vem da pasta), <paramref name="Exe"/> é relativo à pasta, <paramref name="Title"/> é o nome para mostrar
/// (o do VNDB quando confirmado, senão o da pasta). <paramref name="VndbId"/> só existe depois de confirmado; antes disso, <paramref name="Candidates"/> guarda o que o
/// casamento achou e <paramref name="SearchedAt"/> diz que já buscou.
/// </summary>
public sealed record LibraryGame(
    string Id,
    string Folder,
    string? Exe,
    LibraryEngine Engine,
    string Title,
    string? WindowTitle,
    DateTimeOffset AddedAt)
{
    public string? VndbId { get; init; }

    public LibraryConfirmation Confirmation { get; init; }

    public double? MatchScore { get; init; }

    public DateTimeOffset? SearchedAt { get; init; }

    public IReadOnlyList<LibraryCandidate> Candidates { get; init; } = [];

    public LibraryVndbInfo? Info { get; init; }

    /// <summary>Pasta do projeto de tradução ligado ao jogo, se houver.</summary>
    public string? ProjectPath { get; init; }

    /// <summary>O que o app guarda da lista do VNDB deste jogo (rótulos, voto, datas, notas); use <see cref="CurrentList"/> para ler.</summary>
    public LibraryListState? List { get; init; }
    /// <summary>O jogo só abre direito com a localidade do Windows em japonês: "Jogar" passa pelo Locale Emulator.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequiresJapaneseLocale { get; init; }

    [JsonIgnore]
    public bool IsConfirmed => VndbId is not null;

    /// <summary>O estado da lista, se ele é do jogo do VNDB confirmado agora; um estado de outro jogo (a confirmação mudou) não vale.</summary>
    [JsonIgnore]
    public LibraryListState? CurrentList => List is { } state && state.VisualNovelId == VndbId ? state : null;
}

/// <summary>
/// O que a Biblioteca esconde por padrão. Vale para a tela e para a CLI: ficam nos dados para que as duas respeitem a mesma escolha.
/// Spoiler: tags com spoiler e trechos <c>[spoiler]</c> da sinopse. Adulto: a capa de jogo marcado como adulto e toda tag erótica (categoria <c>ero</c>), de qualquer jogo.
/// </summary>
public sealed record LibraryPrivacy(bool ShowSpoilers = false, bool ShowAdult = false);
