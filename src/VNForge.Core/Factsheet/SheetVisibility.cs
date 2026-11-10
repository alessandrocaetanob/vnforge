using VNForge.Core.Library;

namespace VNForge.Core.Factsheet;

/// <summary>
/// O quanto da ficha aparece. O VNDB marca o spoiler em três níveis (0 nenhum, 1 pequeno, 2 grande) e a tela deixa a pessoa subir um de cada vez: <see cref="SpoilerLevel"/> é o
/// maior nível que se vê. Os trechos <c>[spoiler]</c> dos textos, o traço que o VNDB marca como mentira e o gênero real não têm nível e só aparecem no máximo (<see cref="ShowsSpoilers"/>).
/// <see cref="LibraryPrivacy"/> só diz "tudo ou nada"; <see cref="From"/> converte.
/// </summary>
public sealed record SheetVisibility
{
    public const int MaxSpoilerLevel = 2;

    public SheetVisibility(int spoilerLevel = 0, AdultMode adult = AdultMode.Blur)
    {
        SpoilerLevel = Math.Clamp(spoilerLevel, 0, MaxSpoilerLevel);
        Adult = adult;
    }

    public int SpoilerLevel { get; }

    /// <summary>O modo adulto da privacidade: a ficha só mostra tag erótica, traço sexual e retrato adulto no <see cref="AdultMode.Show"/>.</summary>
    public AdultMode Adult { get; }

    public bool ShowAdult => Adult == AdultMode.Show;

    /// <summary>O nível mais alto: os trechos de spoiler dos textos, as mentiras e o gênero real aparecem.</summary>
    public bool ShowsSpoilers => SpoilerLevel >= MaxSpoilerLevel;

    /// <summary>Spoiler liberado na Biblioteca vale o nível máximo; escondido, nenhum.</summary>
    public static SheetVisibility From(LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(privacy);
        return new SheetVisibility(privacy.ShowSpoilers ? MaxSpoilerLevel : 0, privacy.Adult);
    }

    /// <summary>A privacidade equivalente, para as regras da Biblioteca que só conhecem o tudo ou nada (imagens, tags eróticas).</summary>
    public LibraryPrivacy ToPrivacy() => new(ShowsSpoilers, Adult);

    /// <summary>O nome do nível na tela: Escondido, Leve ou Tudo.</summary>
    public static string LevelName(int level) => level switch
    {
        <= 0 => "Escondido",
        1 => "Leve",
        _ => "Tudo",
    };
}
