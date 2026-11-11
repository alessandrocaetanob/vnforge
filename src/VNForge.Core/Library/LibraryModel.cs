using System.Text.Json.Serialization;
using VNForge.Core.Vndb;

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
public sealed record LibraryCandidate(string VndbId, string Title, string? OriginalTitle, string? Released, double Score, IReadOnlyList<string> Evidence)
{
    /// <summary>A capa do candidato, com a marca de conteúdo adulto do VNDB; nula quando o VNDB não tem imagem ou o casamento foi gravado antes de a Biblioteca guardá-la.</summary>
    public VndbImageRef? Image { get; init; }

    /// <summary>A faixa de duração do VNDB (1 a 5) e os minutos votados; nulos sem votos.</summary>
    public int? Length { get; init; }

    public int? LengthMinutes { get; init; }

    /// <summary>A nota do VNDB (de 10 a 100); nula quando ninguém votou.</summary>
    public double? Rating { get; init; }
}

/// <summary>Uma tag do VNDB no jogo. <paramref name="Spoiler"/> é 0 (nenhum), 1 (pequeno) ou 2 (grande).</summary>
public sealed record LibraryTag(string Id, string Name, string Category, double Rating, int Spoiler);

/// <summary>
/// O que a Biblioteca mostra do jogo do VNDB: capa, sinopse, duração e tags. A sinopse vem com os códigos do VNDB (<c>[spoiler]</c>) sem converter;
/// quem mostra passa por <see cref="LibraryVisibility"/>. <paramref name="Adult"/> marca capa ou tags de conteúdo adulto. <paramref name="Rating"/> é a nota do VNDB
/// (de 10 a 100, <see cref="VndbRating.Label"/> a escreve), nula quando ninguém votou ou o jogo foi confirmado antes de a Biblioteca guardá-la.
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
    bool Adult,
    double? Rating = null);

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

    /// <summary>O que o VNDB tinha de tradução em português deste jogo na última conferência da fila de tradução; use <see cref="CurrentPortuguese"/> para ler.</summary>
    public LibraryPortuguese? Portuguese { get; init; }

    /// <summary>O jogo só abre direito com a localidade do Windows em japonês: "Jogar" passa pelo Locale Emulator.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequiresJapaneseLocale { get; init; }

    /// <summary>O exe do jogo só abre depois de um lançador (outro programa que o inicia): o tempo de jogo segue o processo do jogo, não o do lançador.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OpensLauncher { get; init; }

    [JsonIgnore]
    public bool IsConfirmed => VndbId is not null;

    /// <summary>O estado da lista, se ele é do jogo do VNDB confirmado agora; um estado de outro jogo (a confirmação mudou) não vale.</summary>
    [JsonIgnore]
    public LibraryListState? CurrentList => List is { } state && state.VisualNovelId == VndbId ? state : null;

    /// <summary>A conferência de português, se é do jogo do VNDB confirmado agora; a de outro jogo (a confirmação mudou) não vale.</summary>
    [JsonIgnore]
    public LibraryPortuguese? CurrentPortuguese => Portuguese is { } found && found.VisualNovelId == VndbId ? found : null;
}

/// <summary>O que a Biblioteca faz com jogo de conteúdo adulto (capa ou tags marcadas no VNDB). Vale para a tela e para a CLI.</summary>
public enum AdultMode
{
    /// <summary>Capa, tags, traços e imagens aparecem como vêm do VNDB.</summary>
    Show,

    /// <summary>
    /// O jogo segue na Biblioteca, mas a capa e as imagens ficam borradas e as tags eróticas e os traços sexuais ficam escondidos, até a pessoa pedir. A CLI não desenha, então
    /// no lugar do borrão ela esconde a capa e as imagens, como sempre fez.
    /// </summary>
    Blur,

    /// <summary>O jogo adulto sai da grade, da lista, da busca, de "Continuar jogando", da fila de tradução, dos personagens e do catálogo.</summary>
    Hide,
}

/// <summary>
/// O que a Biblioteca esconde por padrão. Vale para a tela e para a CLI: ficam nos dados para que as duas respeitem a mesma escolha.
/// Spoiler: tags com spoiler e trechos <c>[spoiler]</c> da sinopse. Adulto: <see cref="AdultMode"/>; quem nunca escolheu fica no <see cref="AdultMode.Blur"/>.
/// O <c>library.json</c> antigo guardava só <c>showAdult</c> (verdadeiro vira <see cref="AdultMode.Show"/>, o resto <see cref="AdultMode.Blur"/>); a leitura e a gravação estão em <see cref="LibraryPrivacyConverter"/>.
/// </summary>
[JsonConverter(typeof(LibraryPrivacyConverter))]
public sealed record LibraryPrivacy(bool ShowSpoilers = false, AdultMode Adult = AdultMode.Blur)
{
    /// <summary>Capa, tags, traços e imagens adultos aparecem sem esconder nem borrar: só no <see cref="AdultMode.Show"/>.</summary>
    [JsonIgnore]
    public bool ShowAdult => Adult == AdultMode.Show;

    /// <summary>O modo <see cref="AdultMode.Hide"/> tira este jogo de toda lista: ele é adulto e o jogo do VNDB confirmado diz isso.</summary>
    public bool Hides(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return Adult == AdultMode.Hide && game.Info is { Adult: true };
    }

    /// <summary>Os jogos que a privacidade deixa nas listas, na ordem de <paramref name="games"/>.</summary>
    public IReadOnlyList<LibraryGame> Listed(IEnumerable<LibraryGame> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        return [.. games.Where(g => !Hides(g))];
    }
}
