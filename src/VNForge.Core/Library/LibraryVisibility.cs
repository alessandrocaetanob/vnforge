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
/// Aplica a privacidade da biblioteca. Spoiler escondido: tags com spoiler e os trechos <c>[spoiler]</c> da sinopse. Adulto escondido: a capa de jogo marcado como
/// adulto e as tags eróticas dele. A sinopse sai em texto simples (os códigos de formatação do VNDB são tirados).
/// </summary>
public static partial class LibraryVisibility
{
    public const string SpoilerPlaceholder = "[spoiler escondido]";

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
        var visible = info.Tags
            .Where(t => privacy.ShowSpoilers || t.Spoiler == 0)
            .Where(t => !hideAdult || t.Category != "ero")
            .ToList();
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

    /// <summary>A sinopse do VNDB em texto simples. Sem <paramref name="showSpoilers"/>, cada trecho <c>[spoiler]...[/spoiler]</c> vira <see cref="SpoilerPlaceholder"/>.</summary>
    public static string PlainSynopsis(string description, bool showSpoilers, out bool hadSpoilers)
    {
        ArgumentNullException.ThrowIfNull(description);
        hadSpoilers = SpoilerBlock().IsMatch(description);
        var text = SpoilerBlock().Replace(description, showSpoilers ? "$1" : SpoilerPlaceholder);
        text = Link().Replace(text, "$1");
        text = Tag().Replace(text, string.Empty);
        return text.Trim();
    }

    [GeneratedRegex(@"\[spoiler\](.*?)\[/spoiler\]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeoutMilliseconds)]
    private static partial Regex SpoilerBlock();

    [GeneratedRegex(@"\[url=[^\]]*\](.*?)\[/url\]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeoutMilliseconds)]
    private static partial Regex Link();

    [GeneratedRegex(@"\[/?(?:b|i|u|s|quote|raw|code|url|from)(?:\s[^\]]*)?\]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
    private static partial Regex Tag();
}
