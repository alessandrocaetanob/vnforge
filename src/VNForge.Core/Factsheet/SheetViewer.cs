using VNForge.Core.Library;
using VNForge.Core.Text;
using VNForge.Core.Vndb;

namespace VNForge.Core.Factsheet;

/// <summary>
/// Um texto pronto para mostrar. <paramref name="Translated"/> diz que é a tradução (senão é o original do VNDB, ainda sem tradução); <paramref name="HasHiddenSpoilers"/> diz que
/// havia trechos de spoiler e eles foram trocados por <see cref="LibraryVisibility.SpoilerPlaceholder"/>.
/// </summary>
public sealed record SheetShownText(string Text, TranslationStatus Status, bool Translated, bool Outdated, bool HasHiddenSpoilers);

public sealed record SheetTagCard(string Id, string Name, string OriginalName, string Category, double Rating, int Spoiler, TranslationStatus Status);

/// <param name="Lie">O VNDB marca o traço como mentira da história; só aparece com os spoilers no máximo.</param>
public sealed record SheetTraitCard(string Id, string Name, string OriginalName, string Group, int Spoiler, TranslationStatus Status, bool Lie = false);

/// <summary>Um personagem que a privacidade escondeu: o cartão fica no lugar, sem nome, e <paramref name="Spoiler"/> diz de que nível é o spoiler.</summary>
public sealed record SheetHiddenCharacter(string Id, int Spoiler);

/// <summary>
/// Um personagem como a ficha o mostra. <paramref name="Gender"/> é o que aparece sem spoiler; <paramref name="RealGender"/> só vem quando os spoilers estão no máximo e o VNDB
/// marca outro gênero como o real. <paramref name="ImageHidden"/> diz que há retrato e ele foi escondido por ser de conteúdo adulto; <paramref name="Image"/> vem nulo nesse caso.
/// <paramref name="Status"/> é a etapa mais atrasada entre a descrição e os nomes dos traços que aparecem; nulo quando o personagem não tem nenhum desses textos.
/// </summary>
public sealed record SheetCharacterCard(
    string Id,
    string Name,
    string? Original,
    string Role,
    string RoleCode,
    int Spoiler,
    VndbImageRef? Image,
    bool ImageHidden,
    SheetShownText? Description,
    string Gender,
    string? RealGender,
    IReadOnlyList<SheetTraitCard> Traits,
    int HiddenTraits,
    TranslationStatus? Status = null);

/// <summary>Quantos textos da ficha estão em cada etapa. <paramref name="Pending"/> é o que ainda não tem tradução.</summary>
public sealed record SheetStatusCounts(int Pending, int Machine, int Translated, int Reviewed, int Approved)
{
    public int Total => Pending + Machine + Translated + Reviewed + Approved;

    public static SheetStatusCounts Of(IEnumerable<SheetText> texts) => OfStatuses(texts.Select(t => t.Status));

    /// <summary>A mesma contagem a partir das etapas já conhecidas (a de um bloco da revisão é a mais atrasada dos textos dele).</summary>
    public static SheetStatusCounts OfStatuses(IEnumerable<TranslationStatus> statuses)
    {
        var counts = new int[5];
        foreach (var status in statuses)
        {
            counts[status switch
            {
                TranslationStatus.Machine => 1,
                TranslationStatus.Translated => 2,
                TranslationStatus.Reviewed => 3,
                TranslationStatus.Approved => 4,
                _ => 0,
            }]++;
        }

        return new SheetStatusCounts(counts[0], counts[1], counts[2], counts[3], counts[4]);
    }
}

/// <summary>
/// A ficha do jogo depois de esconder o que a <see cref="SheetVisibility"/> esconde. <paramref name="HiddenTags"/> e <paramref name="HiddenCharacters"/> contam o que sumiu;
/// <paramref name="HiddenSpoilerTags"/> e <paramref name="HiddenAdultTags"/> separam as tags escondidas pelo motivo, e <paramref name="HiddenCharacterCards"/> diz quais personagens
/// sumiram e de que nível. <paramref name="TagsStatus"/> é a etapa mais atrasada entre os nomes das tags que aparecem; nulo quando não há tag.
/// </summary>
public sealed record GameSheetCard(
    string VisualNovelId,
    string Title,
    string? OriginalTitle,
    SheetShownText? Synopsis,
    string Length,
    IReadOnlyList<SheetTagCard> Tags,
    int HiddenTags,
    IReadOnlyList<SheetCharacterCard> Characters,
    int HiddenCharacters,
    bool CharactersTruncated,
    SheetStatusCounts Counts,
    int HiddenSpoilerTags,
    int HiddenAdultTags,
    IReadOnlyList<SheetHiddenCharacter> HiddenCharacterCards,
    string LengthDetail,
    string? Released,
    string? Rating,
    TranslationStatus? TagsStatus);

/// <summary>
/// Aplica a privacidade da biblioteca à ficha. O spoiler se esconde pelo nível que o VNDB marca (0 nenhum, 1 pequeno, 2 grande): acima de <see cref="SheetVisibility.SpoilerLevel"/>
/// some, e os trechos <c>[spoiler]</c> dos textos e as mentiras só aparecem no máximo. Vale para o personagem naquele jogo, para cada traço, para a tag e para os textos. O conteúdo adulto
/// some pela mesma regra da Biblioteca: tags eróticas, retrato com marca sexual ou de violência e os traços que o VNDB marca como sexuais (<see cref="VndbTrait.Sexual"/>); o traço sem o
/// campo, de ficha gravada antes dele, some até a próxima busca.
/// </summary>
public static class SheetViewer
{
    private const string EroticCategory = "ero";

    /// <summary>O maior nível de spoiler que a privacidade deixa ver.</summary>
    public static int MaxSpoilerLevel(LibraryPrivacy privacy) => SheetVisibility.From(privacy).SpoilerLevel;

    /// <summary>O mesmo jogo para a tela e a CLI, com a privacidade da Biblioteca (spoiler tudo ou nada).</summary>
    public static GameSheetCard Card(GameSheet game, IEnumerable<CharacterSheet> characters, SheetTerms terms, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(privacy);
        return Card(game, characters, terms, SheetVisibility.From(privacy));
    }

    /// <summary>O mesmo jogo para a tela e a CLI. <paramref name="characters"/> são os do jogo, em qualquer ordem; a ordem de <see cref="GameSheet.CharacterIds"/> vale.</summary>
    public static GameSheetCard Card(GameSheet game, IEnumerable<CharacterSheet> characters, SheetTerms terms, SheetVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(visibility);
        var byId = characters.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var all = game.CharacterIds.Select(id => byId.GetValueOrDefault(id)).OfType<CharacterSheet>().ToList();

        var visibleTags = game.Tags.Where(t => !IsTagHidden(t, visibility)).Select(t => TagCard(t, terms)).ToList();
        var hiddenTags = game.Tags.Where(t => IsTagHidden(t, visibility)).ToList();
        var shown = all.Where(c => (c.In(game.VisualNovelId)?.Spoiler ?? 0) <= visibility.SpoilerLevel).ToList();
        var visibleCharacters = shown.Select(c => CharacterCard(c, game.VisualNovelId, terms, visibility)).ToList();
        var hiddenCharacters = all.Except(shown).Select(c => new SheetHiddenCharacter(c.Id, c.In(game.VisualNovelId)?.Spoiler ?? 0)).ToList();

        var texts = new List<SheetText>();
        texts.AddRange(new[] { game.Synopsis }.OfType<SheetText>());
        texts.AddRange(all.Select(c => c.Description).OfType<SheetText>());
        texts.AddRange(game.Tags.Select(t => terms.Find(SheetTermKind.Tag, t.Id)?.Text).OfType<SheetText>());
        texts.AddRange(all.SelectMany(c => c.Traits).Select(t => t.Id).Distinct(StringComparer.Ordinal).Select(id => terms.Find(SheetTermKind.Trait, id)?.Text).OfType<SheetText>());

        return new GameSheetCard(
            game.VisualNovelId,
            game.Title,
            game.OriginalTitle,
            game.Synopsis is { } synopsis ? Shown(synopsis, visibility) : null,
            SheetLabels.Length(game.LengthMinutes, game.Length),
            visibleTags,
            hiddenTags.Count,
            visibleCharacters,
            hiddenCharacters.Count,
            game.CharactersTruncated,
            SheetStatusCounts.Of(texts),
            hiddenTags.Count(t => !IsAdultTag(t, visibility)),
            hiddenTags.Count(t => IsAdultTag(t, visibility)),
            hiddenCharacters,
            SheetLabels.LengthDetail(game.LengthMinutes, game.Length, game.LengthVotes),
            SheetLabels.Released(game.Released, game.OriginalLanguage),
            VndbRating.Score(game.Rating),
            visibleTags.Count == 0 ? null : visibleTags.Min(t => t.Status));
    }

    /// <summary>A visibilidade esconde a tag: nível de spoiler acima do liberado, ou tag erótica sem o conteúdo adulto liberado.</summary>
    public static bool IsTagHidden(LibraryTag tag, SheetVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(visibility);
        return tag.Spoiler > visibility.SpoilerLevel || IsAdultTag(tag, visibility);
    }

    private static bool IsAdultTag(LibraryTag tag, SheetVisibility visibility) => !visibility.ShowAdult && tag.Category == EroticCategory;

    /// <summary>A privacidade esconde o traço: nível de spoiler acima do liberado, mentira sem os spoilers liberados, ou traço sexual (ou sem o campo, de ficha antiga, até a próxima busca) sem o conteúdo adulto liberado.</summary>
    public static bool IsTraitHidden(VndbTrait trait, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(trait);
        return IsTraitHidden(trait.Spoiler, trait.Lie, trait.Sexual, privacy);
    }

    /// <inheritdoc cref="IsTraitHidden(VndbTrait, LibraryPrivacy)"/>
    public static bool IsTraitHidden(int spoiler, bool lie, bool? sexual, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(privacy);
        return IsTraitHidden(spoiler, lie, sexual, SheetVisibility.From(privacy));
    }

    /// <inheritdoc cref="IsTraitHidden(VndbTrait, LibraryPrivacy)"/>
    public static bool IsTraitHidden(VndbTrait trait, SheetVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(trait);
        return IsTraitHidden(trait.Spoiler, trait.Lie, trait.Sexual, visibility);
    }

    /// <inheritdoc cref="IsTraitHidden(VndbTrait, LibraryPrivacy)"/>
    public static bool IsTraitHidden(int spoiler, bool lie, bool? sexual, SheetVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        return spoiler > visibility.SpoilerLevel
            || (lie && !visibility.ShowsSpoilers)
            || (sexual != false && !visibility.ShowAdult);
    }

    /// <summary>O texto que a privacidade deixa ver: a tradução quando há, senão o original; os trechos de spoiler somem sem <see cref="LibraryPrivacy.ShowSpoilers"/>.</summary>
    public static SheetShownText Shown(SheetText text, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(privacy);
        return Shown(text, SheetVisibility.From(privacy));
    }

    /// <summary>O texto que a visibilidade deixa ver: a tradução quando há, senão o original; os trechos de spoiler só aparecem no nível máximo.</summary>
    public static SheetShownText Shown(SheetText text, SheetVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(visibility);
        return Shown(text.Shown, text, visibility);
    }

    /// <summary>O original do VNDB no lugar da tradução, com a mesma regra de spoiler: o que "Ver o original" mostra.</summary>
    public static SheetShownText ShownSource(SheetText text, SheetVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(visibility);
        return Shown(text.Source, text, visibility) with { Translated = false };
    }

    private static SheetShownText Shown(string value, SheetText text, SheetVisibility visibility)
    {
        var plain = LibraryVisibility.PlainSynopsis(value, visibility.ShowsSpoilers, out var hadSpoilers);
        return new SheetShownText(plain, text.Status, text.HasTarget, text.Outdated, hadSpoilers && !visibility.ShowsSpoilers);
    }

    private static SheetTagCard TagCard(LibraryTag tag, SheetTerms terms)
    {
        var term = terms.Find(SheetTermKind.Tag, tag.Id);
        return new SheetTagCard(tag.Id, term?.Text.Shown ?? tag.Name, tag.Name, tag.Category, tag.Rating, tag.Spoiler, term?.Text.Status ?? TranslationStatus.Untranslated);
    }

    private static SheetCharacterCard CharacterCard(CharacterSheet character, string visualNovelId, SheetTerms terms, SheetVisibility visibility)
    {
        var appearance = character.In(visualNovelId);
        var imageHidden = character.Image is { } image && VndbImageVisibility.IsHidden(image, visibility.ToPrivacy());
        var traits = character.Traits.Where(t => !IsTraitHidden(t, visibility)).Select(t => TraitCard(t, terms)).ToList();
        var visible = character.Gender?.Visible;
        var real = character.Gender?.Spoiler;
        var statuses = traits.Select(t => t.Status).ToList();
        if (character.Description is { } description)
        {
            statuses.Add(description.Status);
        }

        return new SheetCharacterCard(
            character.Id,
            character.Name,
            character.Original,
            SheetLabels.Role(appearance?.Role ?? "appears"),
            appearance?.Role ?? "appears",
            appearance?.Spoiler ?? 0,
            imageHidden ? null : character.Image,
            imageHidden,
            character.Description is { } text ? Shown(text, visibility) : null,
            SheetLabels.Gender(visible),
            visibility.ShowsSpoilers && real is not null && real != visible ? SheetLabels.Gender(real) : null,
            traits,
            character.Traits.Count - traits.Count,
            statuses.Count == 0 ? null : statuses.Min());
    }

    private static SheetTraitCard TraitCard(VndbTrait trait, SheetTerms terms)
    {
        var term = terms.Find(SheetTermKind.Trait, trait.Id);
        return new SheetTraitCard(trait.Id, term?.Text.Shown ?? trait.Name, trait.Name, trait.GroupName, trait.Spoiler, term?.Text.Status ?? TranslationStatus.Untranslated, trait.Lie);
    }
}
