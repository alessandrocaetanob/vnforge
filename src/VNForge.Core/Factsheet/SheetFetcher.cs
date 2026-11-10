using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.Core.Factsheet;

/// <summary>O que buscar a ficha gravou: a ficha do jogo, quantos personagens e quantos termos (tags e traços) eram novos e ainda precisam de tradução.</summary>
public sealed record SheetFetchResult(GameSheet Sheet, int Characters, int NewTerms);

/// <summary>
/// Traz o jogo e os personagens do VNDB para a ficha. Buscar de novo atualiza os originais e <b>mantém as traduções</b>: um texto que o VNDB não mudou fica como está, e um
/// que mudou segue as regras de <see cref="SheetText.WithSource"/>. Os nomes de tag e de traço que já foram traduzidos para outro jogo não voltam a pendente.
/// </summary>
public static class SheetFetcher
{
    /// <summary>Busca o jogo e os personagens no VNDB e grava a ficha.</summary>
    /// <exception cref="LibraryException">O VNDB não tem o jogo, ou um arquivo de ficha está ilegível.</exception>
    /// <exception cref="VndbException">O VNDB não respondeu.</exception>
    public static async Task<SheetFetchResult> FetchAsync(
        IVndbCatalog catalog, SheetStore store, string visualNovelId, TimeProvider? time = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(store);
        var vn = await catalog.GetVisualNovelAsync(visualNovelId, cancellationToken).ConfigureAwait(false)
            ?? throw new LibraryException($"O VNDB não tem o jogo {visualNovelId}.");
        var characters = await catalog.GetCharactersAsync(visualNovelId, cancellationToken).ConfigureAwait(false);
        return store.Update(current => Merge(current, vn, characters, (time ?? TimeProvider.System).GetUtcNow()));
    }

    /// <summary>
    /// A mesma gravação de <see cref="FetchAsync"/> a partir do jogo e dos personagens que o chamador já tem. Chame-a de dentro de <see cref="SheetStore.Update{T}"/>.
    /// </summary>
    public static SheetFetchResult Merge(SheetStore store, VndbVisualNovel vn, VndbResults<VndbCharacter> characters, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(vn);
        ArgumentNullException.ThrowIfNull(characters);
        var previous = store.LoadGame(vn.Id);
        var terms = store.LoadTerms();
        var newTerms = 0;

        var tags = LibraryMatcher.InfoOf(vn).Tags;
        foreach (var tag in tags)
        {
            newTerms += terms.Ensure(SheetTermKind.Tag, tag.Id, tag.Name, tag.Category) ? 1 : 0;
        }

        var merged = new List<CharacterSheet>();
        foreach (var vndb in characters.Items)
        {
            foreach (var trait in vndb.Traits)
            {
                newTerms += terms.Ensure(SheetTermKind.Trait, trait.Id, trait.Name, trait.GroupName) ? 1 : 0;
            }

            var character = Character(vndb, vn.Id, store.LoadCharacter(vndb.Id));
            store.SaveCharacter(character);
            merged.Add(character);
        }

        var source = SheetMarkup.Normalize(vn.Description);
        var synopsis = source is null ? null : previous?.Synopsis?.WithSource(source) ?? SheetText.Pending(source);
        var sheet = new GameSheet(
            vn.Id,
            vn.Title,
            vn.AltTitle,
            synopsis,
            vn.Length,
            vn.LengthMinutes,
            vn.LengthVotes,
            tags,
            [.. merged
                .OrderBy(c => SheetLabels.RoleRank(c.In(vn.Id)?.Role ?? string.Empty))
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .Select(c => c.Id)],
            characters.Truncated,
            now,
            vn.Released,
            vn.OriginalLanguage,
            vn.Rating);
        store.SaveGame(sheet);
        store.SaveTerms(terms);
        return new SheetFetchResult(sheet, merged.Count, newTerms);
    }

    private static CharacterSheet Character(VndbCharacter vndb, string visualNovelId, CharacterSheet? existing)
    {
        var source = SheetMarkup.Normalize(vndb.Description);
        var description = source is null ? null : existing?.Description?.WithSource(source) ?? SheetText.Pending(source);
        var appearances = vndb.Appearances
            .GroupBy(a => a.VisualNovelId, StringComparer.Ordinal)
            .Select(g => new SheetAppearance(
                g.Key,
                g.Select(a => a.Role).MinBy(SheetLabels.RoleRank)!,
                g.Max(a => a.Spoiler),
                [.. g.Select(a => new SheetAppearanceEdition(a.ReleaseId, a.Role, a.Spoiler)).DistinctBy(e => e.ReleaseId, StringComparer.Ordinal)]))
            .OrderBy(a => a.VisualNovelId, StringComparer.Ordinal)
            .ToList();
        if (appearances.TrueForAll(a => a.VisualNovelId != visualNovelId))
        {
            appearances.Add(new SheetAppearance(visualNovelId, "appears", 0, [new SheetAppearanceEdition(null, "appears", 0)]));
        }

        return new CharacterSheet(
            vndb.Id,
            vndb.Name,
            string.IsNullOrWhiteSpace(vndb.Original) ? null : vndb.Original.Trim(),
            [.. vndb.Aliases.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim())],
            vndb.Gender,
            VndbImageRef.Portrait(vndb.Image),
            description,
            vndb.Traits,
            appearances);
    }
}
