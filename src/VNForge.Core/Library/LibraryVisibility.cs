using System.Text.RegularExpressions;

namespace VNForge.Core.Library;

/// <summary>
/// O jogo como a tela e a CLI podem mostrar, depois de esconder o que a <see cref="LibraryPrivacy"/> esconde. <paramref name="HiddenTags"/> conta as tags
/// escondidas; <paramref name="CoverHidden"/> diz que há capa e ela foi escondida por ser de conteúdo adulto; <paramref name="SynopsisHasSpoilers"/> diz que a sinopse
/// tinha trechos de spoiler e eles foram trocados por um aviso.
/// </summary>
public sealed record LibraryGameCard(
    string Id,
    string Title,
    string? OriginalTitle,
    string? Released,
    string? CoverUrl,
    bool CoverHidden,
    string? Synopsis,
    bool SynopsisHasSpoilers,
    int? Length,
    int? LengthMinutes,
    IReadOnlyList<LibraryTag> Tags,
    int HiddenTags,
    bool Adult);

/// <summary>
/// Aplica a privacidade da biblioteca. Spoiler escondido: tags com spoiler e os trechos <c>[spoiler]</c> da sinopse; um <c>[spoiler]</c> sem <c>[/spoiler]</c> vale até o fim
/// do texto. Adulto escondido: a capa de jogo marcado como adulto e toda tag erótica (categoria <c>ero</c> do VNDB), de qualquer jogo, marcado como adulto ou não. A sinopse sai em texto simples (os códigos de formatação do VNDB são tirados).
/// </summary>
public static partial class LibraryVisibility
{
    public const string SpoilerPlaceholder = "[spoiler escondido]";

    /// <summary>A categoria do VNDB para tags de conteúdo sexual.</summary>
    private const string EroticCategory = "ero";

    /// <summary>Marca (caractere de uso privado) o lugar do spoiler escondido até o fim da limpeza, para que o aviso não seja tomado por código do VNDB.</summary>
    private const string Sentinel = "";

    private const int RegexTimeoutMilliseconds = 500;

    /// <summary>A ficha do jogo para mostrar, ou nulo se o jogo ainda não tem jogo do VNDB confirmado.</summary>
    public static LibraryGameCard? Card(LibraryGame game, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(privacy);
        if (game.Info is not { } info)
        {
            return null;
        }

        var hideAdult = info.Adult && !privacy.ShowAdult;
        var visible = VisibleTags(info, privacy);
        var hadSpoilers = false;
        var synopsis = info.Synopsis is null ? null : PlainSynopsis(info.Synopsis, privacy.ShowSpoilers, out hadSpoilers);
        return new LibraryGameCard(
            game.Id,
            info.Title,
            info.OriginalTitle,
            info.Released,
            hideAdult ? null : info.CoverUrl,
            hideAdult && info.CoverUrl is not null,
            synopsis,
            hadSpoilers && !privacy.ShowSpoilers,
            info.Length,
            info.LengthMinutes,
            visible,
            info.Tags.Count - visible.Count,
            info.Adult);
    }

    /// <summary>As tags do VNDB que a privacidade deixa ver; vazio se o jogo ainda não tem jogo do VNDB confirmado. É o que a ficha mostra e o que o filtro por tag enxerga.</summary>
    public static IReadOnlyList<LibraryTag> VisibleTags(LibraryGame game, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(privacy);
        return game.Info is { } info ? VisibleTags(info, privacy) : [];
    }

    /// <summary>A privacidade esconde a tag: ela tem spoiler e <see cref="LibraryPrivacy.ShowSpoilers"/> é falso, ou é erótica e <see cref="LibraryPrivacy.ShowAdult"/> é falso.</summary>
    public static bool IsHidden(LibraryTag tag, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(privacy);
        return (!privacy.ShowSpoilers && tag.Spoiler != 0) || (!privacy.ShowAdult && tag.Category == EroticCategory);
    }

    private static List<LibraryTag> VisibleTags(LibraryVndbInfo info, LibraryPrivacy privacy) =>
        [.. info.Tags.Where(t => !IsHidden(t, privacy))];

    /// <summary>A sinopse do VNDB em texto simples. Sem <paramref name="showSpoilers"/>, cada trecho <c>[spoiler]...[/spoiler]</c> vira <see cref="SpoilerPlaceholder"/>.</summary>
    public static string PlainSynopsis(string description, bool showSpoilers, out bool hadSpoilers)
    {
        ArgumentNullException.ThrowIfNull(description);
        hadSpoilers = SpoilerBlock().IsMatch(description);
        var text = SpoilerBlock().Replace(description, showSpoilers ? "$1" : Sentinel);
        text = Link().Replace(text, "$1");
        text = Tag().Replace(text, string.Empty);
        return text.Replace(Sentinel, SpoilerPlaceholder, StringComparison.Ordinal).Trim();
    }

    [GeneratedRegex(@"\[spoiler\](.*?)(?:\[/spoiler\]|\z)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeoutMilliseconds)]
    private static partial Regex SpoilerBlock();

    [GeneratedRegex(@"\[url=[^\]]*\](.*?)\[/url\]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeoutMilliseconds)]
    private static partial Regex Link();

    [GeneratedRegex(@"\[/?(?:b|i|u|s|quote|raw|code|url|from|spoiler)(?:\s[^\]]*)?\]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
    private static partial Regex Tag();
}
