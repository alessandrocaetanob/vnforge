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

public sealed record SheetTraitCard(string Id, string Name, string OriginalName, string Group, int Spoiler, TranslationStatus Status);

/// <summary>
/// Um personagem como a ficha o mostra. <paramref name="Gender"/> é o que aparece sem spoiler; <paramref name="RealGender"/> só vem quando os spoilers estão liberados e o VNDB
/// marca outro gênero como o real. <paramref name="ImageHidden"/> diz que há retrato e ele foi escondido por ser de conteúdo adulto; <paramref name="Image"/> vem nulo nesse caso.
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
    int HiddenTraits);

/// <summary>Quantos textos da ficha estão em cada etapa. <paramref name="Pending"/> é o que ainda não tem tradução.</summary>
public sealed record SheetStatusCounts(int Pending, int Machine, int Translated, int Reviewed, int Approved)
{
    public int Total => Pending + Machine + Translated + Reviewed + Approved;

    public static SheetStatusCounts Of(IEnumerable<SheetText> texts)
    {
        var counts = new int[5];
        foreach (var text in texts)
        {
            counts[text.Status switch
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
/// A ficha do jogo depois de esconder o que a <see cref="LibraryPrivacy"/> esconde. <paramref name="HiddenTags"/> e <paramref name="HiddenCharacters"/> contam o que sumiu.
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
    SheetStatusCounts Counts);

/// <summary>
/// Aplica a privacidade da biblioteca à ficha. O spoiler se esconde pelo nível que o VNDB marca (0 nenhum, 1 pequeno, 2 grande): sem <see cref="LibraryPrivacy.ShowSpoilers"/> só
/// o nível 0 aparece, e com ele todos. Vale para o personagem naquele jogo, para cada traço, para a tag e para os trechos <c>[spoiler]</c> dos textos. Traço marcado como
/// mentira (<see cref="VndbTrait.Lie"/>) só aparece com os spoilers liberados. O conteúdo adulto some pela mesma regra da Biblioteca: tags eróticas, retrato com marca sexual
/// ou de violência e os traços do grupo <see cref="AdultTraitGroup"/>.
/// </summary>
public static class SheetViewer
{
    /// <summary>O grupo de traços do VNDB para conteúdo sexual.</summary>
    public const string AdultTraitGroup = "Sexual Content";

    /// <summary>O maior nível de spoiler que a privacidade deixa ver.</summary>
    public static int MaxSpoilerLevel(LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(privacy);
        return privacy.ShowSpoilers ? 2 : 0;
    }

    /// <summary>O mesmo jogo para a tela e a CLI. <paramref name="characters"/> são os do jogo, em qualquer ordem; a ordem de <see cref="GameSheet.CharacterIds"/> vale.</summary>
    public static GameSheetCard Card(GameSheet game, IEnumerable<CharacterSheet> characters, SheetTerms terms, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(privacy);
        var max = MaxSpoilerLevel(privacy);
        var byId = characters.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var all = game.CharacterIds.Select(id => byId.GetValueOrDefault(id)).OfType<CharacterSheet>().ToList();

        var visibleTags = game.Tags.Where(t => !LibraryVisibility.IsHidden(t, privacy)).Select(t => TagCard(t, terms)).ToList();
        var visibleCharacters = all.Where(c => (c.In(game.VisualNovelId)?.Spoiler ?? 0) <= max).Select(c => CharacterCard(c, game.VisualNovelId, terms, privacy)).ToList();

        var texts = new List<SheetText>();
        texts.AddRange(new[] { game.Synopsis }.OfType<SheetText>());
        texts.AddRange(all.Select(c => c.Description).OfType<SheetText>());
        texts.AddRange(game.Tags.Select(t => terms.Find(SheetTermKind.Tag, t.Id)?.Text).OfType<SheetText>());
        texts.AddRange(all.SelectMany(c => c.Traits).Select(t => t.Id).Distinct(StringComparer.Ordinal).Select(id => terms.Find(SheetTermKind.Trait, id)?.Text).OfType<SheetText>());

        return new GameSheetCard(
            game.VisualNovelId,
            game.Title,
            game.OriginalTitle,
            game.Synopsis is { } synopsis ? Shown(synopsis, privacy) : null,
            SheetLabels.Length(game.LengthMinutes, game.Length),
            visibleTags,
            game.Tags.Count - visibleTags.Count,
            visibleCharacters,
            all.Count - visibleCharacters.Count,
            game.CharactersTruncated,
            SheetStatusCounts.Of(texts));
    }

    /// <summary>A privacidade esconde o traço: nível de spoiler acima do liberado, mentira sem os spoilers liberados, ou grupo adulto sem o conteúdo adulto liberado.</summary>
    public static bool IsTraitHidden(VndbTrait trait, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(trait);
        ArgumentNullException.ThrowIfNull(privacy);
        return trait.Spoiler > MaxSpoilerLevel(privacy)
            || (trait.Lie && !privacy.ShowSpoilers)
            || (!privacy.ShowAdult && string.Equals(trait.GroupName, AdultTraitGroup, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>O texto que a privacidade deixa ver: a tradução quando há, senão o original; os trechos de spoiler somem sem <see cref="LibraryPrivacy.ShowSpoilers"/>.</summary>
    public static SheetShownText Shown(SheetText text, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(privacy);
        var plain = LibraryVisibility.PlainSynopsis(text.Shown, privacy.ShowSpoilers, out var hadSpoilers);
        return new SheetShownText(plain, text.Status, text.HasTarget, text.Outdated, hadSpoilers && !privacy.ShowSpoilers);
    }

    private static SheetTagCard TagCard(LibraryTag tag, SheetTerms terms)
    {
        var term = terms.Find(SheetTermKind.Tag, tag.Id);
        return new SheetTagCard(tag.Id, term?.Text.Shown ?? tag.Name, tag.Name, tag.Category, tag.Rating, tag.Spoiler, term?.Text.Status ?? TranslationStatus.Untranslated);
    }

    private static SheetCharacterCard CharacterCard(CharacterSheet character, string visualNovelId, SheetTerms terms, LibraryPrivacy privacy)
    {
        var appearance = character.In(visualNovelId);
        var imageHidden = character.Image is { } image && VndbImageVisibility.IsHidden(image, privacy);
        var traits = character.Traits.Where(t => !IsTraitHidden(t, privacy)).Select(t => TraitCard(t, terms)).ToList();
        var visible = character.Gender?.Visible;
        var real = character.Gender?.Spoiler;
        return new SheetCharacterCard(
            character.Id,
            character.Name,
            character.Original,
            SheetLabels.Role(appearance?.Role ?? "appears"),
            appearance?.Role ?? "appears",
            appearance?.Spoiler ?? 0,
            imageHidden ? null : character.Image,
            imageHidden,
            character.Description is { } description ? Shown(description, privacy) : null,
            SheetLabels.Gender(visible),
            privacy.ShowSpoilers && real is not null && real != visible ? SheetLabels.Gender(real) : null,
            traits,
            character.Traits.Count - traits.Count);
    }

    private static SheetTraitCard TraitCard(VndbTrait trait, SheetTerms terms)
    {
        var term = terms.Find(SheetTermKind.Trait, trait.Id);
        return new SheetTraitCard(trait.Id, term?.Text.Shown ?? trait.Name, trait.Name, trait.GroupName, trait.Spoiler, term?.Text.Status ?? TranslationStatus.Untranslated);
    }
}
