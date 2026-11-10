using System.Globalization;
using VNForge.Core.Projects;

namespace VNForge.Core.Library;

/// <summary>Uma tag da gaveta. <paramref name="Mine"/> marca as tags da pessoa (as do VNDB vêm sem a marca).</summary>
public sealed record LibraryDrawerTag(string Text, bool Mine);

/// <summary>
/// A gaveta do jogo escolhido na grade, já em texto. Spoiler e conteúdo adulto saem escondidos pela <see cref="LibraryPrivacy"/>: <paramref name="SpoilerTagsHidden"/> e
/// <paramref name="AdultTagsHidden"/> dizem que há tags que a privacidade escondeu, e <paramref name="SynopsisHasSpoilers"/> que a sinopse teve trechos trocados por aviso.
/// <paramref name="MoreTags"/> conta as tags do VNDB visíveis que não couberam em <see cref="LibraryDrawers.MaxVndbTags"/>.
/// </summary>
public sealed record LibraryDrawer(
    LibraryCard Card,
    string EngineText,
    string VndbText,
    string Duration,
    string? ReleasedYear,
    string? Synopsis,
    bool SynopsisHasSpoilers,
    bool SynopsisReviewed,
    IReadOnlyList<LibraryDrawerTag> Tags,
    int MoreTags,
    bool SpoilerTagsHidden,
    bool AdultTagsHidden,
    string? TranslationText);

public static class LibraryDrawers
{
    /// <summary>Quantas tags do VNDB a gaveta mostra antes do "+N".</summary>
    public const int MaxVndbTags = 3;

    public static LibraryDrawer Describe(LibraryGame game, LibraryPrivacy privacy, UserTags? userTags = null, LibrarySheet? sheet = null, StatusCounts? translation = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(privacy);
        var card = LibraryCards.Describe(game, privacy, translation);
        var info = game.Info;

        string? synopsis = null;
        var hadSpoilers = false;
        var reviewed = false;
        var raw = sheet is { SynopsisTranslated: true, Synopsis: { Length: > 0 } translated } ? translated : info?.Synopsis;
        if (raw is not null)
        {
            synopsis = LibraryVisibility.PlainSynopsis(raw, privacy.ShowSpoilers, out hadSpoilers);
            reviewed = ReferenceEquals(raw, sheet?.Synopsis) && sheet!.SynopsisReviewed;
        }

        var visible = LibraryVisibility.VisibleTags(game, privacy);
        var tags = new List<LibraryDrawerTag>();
        foreach (var tag in visible.Take(MaxVndbTags))
        {
            tags.Add(new LibraryDrawerTag(sheet?.TagNames.GetValueOrDefault(tag.Id) ?? tag.Name, Mine: false));
        }

        if (userTags is not null)
        {
            tags.AddRange(userTags.TagsOf(game.Id).Select(name => new LibraryDrawerTag(name, Mine: true)));
        }

        var hidden = info?.Tags.Where(t => LibraryVisibility.IsHidden(t, privacy)).ToList() ?? [];
        return new LibraryDrawer(
            card,
            EngineText(game.Engine),
            VndbText(game),
            LibraryLength.Short(info?.LengthMinutes, info?.Length) ?? LibraryCards.NoData,
            YearOf(info?.Released),
            synopsis,
            hadSpoilers && !privacy.ShowSpoilers,
            reviewed,
            tags,
            Math.Max(0, visible.Count - MaxVndbTags),
            hidden.Any(t => !privacy.ShowSpoilers && t.Spoiler != 0),
            hidden.Any(t => !privacy.ShowAdult && t.Category == "ero"),
            game.ProjectPath is not null && translation is { Total: > 0 } ? LibraryListing.ReadyLines(translation) : null);
    }

    /// <summary>"Yu-RIS · traduz", "Siglus · sem suporte ainda" ou "Não reconhecida".</summary>
    public static string EngineText(LibraryEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Source switch
        {
            LibraryEngineSource.Engine => $"{engine.Name} · traduz",
            LibraryEngineSource.Signature => $"{engine.Name} · sem suporte ainda",
            _ => "Não reconhecida",
        };
    }

    /// <summary>"v31201 · casado", "a confirmar" quando há candidatos, "sem casamento" quando não há.</summary>
    public static string VndbText(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.VndbId is { } id ? $"{id} · casado" : game.Candidates.Count > 0 ? "a confirmar" : "sem casamento";
    }

    /// <summary>O ano da data de lançamento do VNDB (<c>2019</c>, <c>2019-04-12</c>); nulo para <c>TBA</c> e para o que não começa por um ano.</summary>
    public static string? YearOf(string? released) =>
        released is { Length: >= 4 } text && text[..4].All(char.IsAsciiDigit) && int.Parse(text[..4], CultureInfo.InvariantCulture) > 0 ? text[..4] : null;
}
