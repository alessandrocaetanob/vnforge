using VNForge.Core.Factsheet;
using VNForge.Core.Vndb;

namespace VNForge.Core.Library;

/// <summary>
/// Onde o personagem aparece na biblioteca: um jogo (item da biblioteca, não do VNDB: duas pastas do mesmo jogo são dois itens, cada um com a sua edição), o papel e o nível de
/// spoiler que valem para ele, e as edições casadas com a pasta que decidiram isso (vazio quando nenhuma casou e vale o papel mais importante e o maior spoiler).
/// </summary>
public sealed record LibraryCharacterAppearance(string GameId, string GameTitle, string VisualNovelId, string Role, string RoleCode, int Spoiler, IReadOnlyList<string> Editions);

/// <summary>
/// Um personagem da Biblioteca como a página dele o mostra, com a privacidade já aplicada: texto em português quando existe (senão o original do VNDB), traços de spoiler e
/// conteúdo adulto fora, <paramref name="HiddenAppearances"/> contando os jogos escondidos por spoiler. <paramref name="Image"/> vem nulo quando o retrato foi escondido
/// (<paramref name="ImageHidden"/>), exceto no <see cref="AdultMode.Blur"/>, em que a tela o mostra borrado. Os jogos que o <see cref="AdultMode.Hide"/> tira da Biblioteca não entram nas aparições.
/// </summary>
public sealed record LibraryCharacterCard(
    string Id,
    string Name,
    string? Original,
    IReadOnlyList<string> Aliases,
    VndbImageRef? Image,
    bool ImageHidden,
    SheetShownText? Description,
    string Gender,
    string? RealGender,
    IReadOnlyList<SheetTraitCard> Traits,
    int HiddenTraits,
    IReadOnlyList<LibraryCharacterAppearance> AppearsIn,
    int HiddenAppearances);

/// <summary>
/// Os personagens que a privacidade deixa ver; <paramref name="Hidden"/> conta os que só aparecem em jogos acima do nível de spoiler liberado e casam com o filtro de traço
/// e de jogo. Com uma busca de texto vale 0: contar escondidos depois de buscar um nome revelaria que o personagem existe.
/// </summary>
public sealed record LibraryCharacterList(IReadOnlyList<LibraryCharacterCard> Characters, int Hidden);

/// <summary>O resultado de procurar um personagem pelo id: o cartão, ou <paramref name="HiddenBySpoiler"/> quando ele existe na biblioteca mas só em jogos escondidos.</summary>
public sealed record LibraryCharacterLookup(LibraryCharacterCard? Card, bool HiddenBySpoiler);

/// <summary>
/// O que a lista de personagens procura. <paramref name="Text"/> casa com o nome, o nome original, os apelidos e o título dos jogos em que o personagem aparece, sem diferenciar
/// maiúsculas, acentos de largura nem pontuação. Cada item de <paramref name="Traits"/> é o id do traço (<c>i34</c>) ou um pedaço do nome, em português ou no original; o
/// personagem precisa de todos, ou de qualquer um com <paramref name="AnyTrait"/>. Só contam os traços que a privacidade deixa ver, para o filtro não revelar um spoiler.
/// <paramref name="GameId"/> limita a um jogo da biblioteca.
/// </summary>
public sealed record LibraryCharacterFilter(string? Text = null, IReadOnlyList<string>? Traits = null, bool AnyTrait = false, string? GameId = null);

/// <summary>
/// A área "Personagens" da Biblioteca (d11). O registro de personagens é o das fichas (<see cref="SheetStore"/>): cada personagem do VNDB é um arquivo só, ligado a todos os
/// jogos por <c>vns</c>. Aqui ele se cruza com a biblioteca: só entram os jogos que a pessoa tem (com o jogo do VNDB confirmado), e o personagem só entra se aparece em algum
/// deles. A descrição e os traços vêm da ficha traduzida (d9), então traduzir um personagem uma vez vale para todos os jogos.
/// </summary>
/// <remarks>
/// Papel e spoiler por jogo: o VNDB os dá por edição. O <b>papel</b> vem da edição casada com a pasta (<see cref="GameEditions"/>) ou, se o VNDB só cita outras edições, da
/// entrada que vale para toda edição; sem edição casada vale o papel mais importante. O <b>spoiler</b> é sempre o maior nível entre todas as edições: o casamento da edição é
/// uma estimativa pelo título, e errar para baixo mostraria um spoiler, então ele nunca o baixa (<see cref="SheetAppearance.WorstSpoiler"/>; a ficha sem as edições vale 2).
/// O nível de cada jogo esconde o jogo da lista de "Aparece em" e, se todos os jogos do personagem passam do nível liberado, o personagem inteiro.
/// </remarks>
public static class LibraryCharacters
{
    /// <summary>A lista de personagens da biblioteca com a privacidade aplicada, em ordem de nome e filtrada por <paramref name="filter"/>.</summary>
    /// <exception cref="LibraryException">O filtro tem um texto ou um traço sem letras nem números.</exception>
    public static LibraryCharacterList List(
        GameLibrary library, IEnumerable<CharacterSheet> characters, SheetTerms terms, GameEditions editions, LibraryPrivacy privacy, LibraryCharacterFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(editions);
        ArgumentNullException.ThrowIfNull(privacy);
        var query = Query.From(filter);
        var all = Build(library, characters, terms, editions, privacy, query, out var hidden);
        return new LibraryCharacterList(all, hidden);
    }

    /// <summary>O personagem com esse id. <see cref="LibraryCharacterLookup.Card"/> é nulo se ele não está em nenhum jogo da biblioteca ou se os jogos dele estão escondidos.</summary>
    public static LibraryCharacterLookup Find(
        GameLibrary library, IEnumerable<CharacterSheet> characters, SheetTerms terms, GameEditions editions, LibraryPrivacy privacy, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var visible = Build(library, characters.Where(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)), terms, editions, privacy, Query.From(null), out var hidden);
        return new LibraryCharacterLookup(visible.FirstOrDefault(), hidden > 0);
    }

    /// <summary>
    /// O papel e o spoiler de uma aparição para uma pasta de jogo. <paramref name="matched"/> são as edições casadas com ela e só decidem o papel; o spoiler é o maior entre as
    /// edições, e o de uma ficha sem as edições (gravada antes de a Biblioteca guardá-las) é o pior caso, 2.
    /// </summary>
    public static (string Role, int Spoiler, IReadOnlyList<string> Editions) Resolve(SheetAppearance appearance, IReadOnlyCollection<string> matched)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(matched);
        if (appearance.Editions is not { Count: > 0 } editions)
        {
            return (appearance.Role, appearance.WorstSpoiler, []);
        }

        IReadOnlyList<SheetAppearanceEdition> chosen = editions;
        if (matched.Count > 0)
        {
            var own = editions.Where(e => e.ReleaseId is not null && matched.Contains(e.ReleaseId)).ToList();
            if (own.Count == 0)
            {
                own = [.. editions.Where(e => e.ReleaseId is null)];
            }

            if (own.Count > 0)
            {
                chosen = own;
            }
        }

        var used = chosen.Count == editions.Count ? [] : chosen.Select(e => e.ReleaseId).OfType<string>().Order(StringComparer.Ordinal).ToList();
        return (chosen.Select(e => e.Role).MinBy(SheetLabels.RoleRank)!, appearance.WorstSpoiler, used);
    }

    private static List<LibraryCharacterCard> Build(
        GameLibrary library, IEnumerable<CharacterSheet> characters, SheetTerms terms, GameEditions editions, LibraryPrivacy privacy, Query query, out int hidden)
    {
        var max = SheetViewer.MaxSpoilerLevel(privacy);
        var byVisualNovel = library.Games.Where(g => g.VndbId is not null && !privacy.Hides(g)).ToLookup(g => g.VndbId!, StringComparer.Ordinal);
        var cards = new List<LibraryCharacterCard>();
        hidden = 0;
        foreach (var character in characters)
        {
            var appearances = Appearances(character, byVisualNovel, editions);
            if (appearances.Count == 0)
            {
                continue;
            }

            var visible = appearances.Where(a => a.Spoiler <= max).ToList();
            if (visible.Count == 0)
            {
                // O cartão só serve para ver se o filtro o pegaria: o contador de escondidos é o do resultado, não o da biblioteca. Com busca de texto não se conta nada,
                // porque "1 escondido" depois de uma busca pelo nome revelaria que o personagem existe.
                hidden += query.Text is null && query.Matches(Card(character, appearances, 0, terms, privacy)) ? 1 : 0;
                continue;
            }

            var card = Card(character, visible, appearances.Count - visible.Count, terms, privacy);
            if (query.Matches(card))
            {
                cards.Add(card);
            }
        }

        return [.. cards.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Id, StringComparer.Ordinal)];
    }

    private static List<LibraryCharacterAppearance> Appearances(CharacterSheet character, ILookup<string, LibraryGame> byVisualNovel, GameEditions editions)
    {
        var result = new List<LibraryCharacterAppearance>();
        foreach (var appearance in character.Appearances)
        {
            foreach (var game in byVisualNovel[appearance.VisualNovelId])
            {
                var (role, spoiler, used) = Resolve(appearance, editions.CurrentFor(game));
                result.Add(new LibraryCharacterAppearance(game.Id, game.Title, appearance.VisualNovelId, SheetLabels.Role(role), role, spoiler, used));
            }
        }

        return [.. result
            .OrderBy(a => SheetLabels.RoleRank(a.RoleCode))
            .ThenBy(a => a.GameTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.GameId, StringComparer.Ordinal)];
    }

    private static LibraryCharacterCard Card(CharacterSheet character, List<LibraryCharacterAppearance> visible, int hiddenAppearances, SheetTerms terms, LibraryPrivacy privacy)
    {
        var imageHidden = character.Image is { } image && VndbImageVisibility.IsHidden(image, privacy);
        var traits = character.Traits.Where(t => !SheetViewer.IsTraitHidden(t, privacy)).Select(t => SheetViewer.TraitCard(t, terms)).ToList();
        var visibleGender = character.Gender?.Visible;
        var real = character.Gender?.Spoiler;
        return new LibraryCharacterCard(
            character.Id,
            character.Name,
            character.Original,
            character.Aliases,
            imageHidden && privacy.Adult != AdultMode.Blur ? null : character.Image,
            imageHidden,
            character.Description is { } description ? SheetViewer.Shown(description, privacy) : null,
            SheetLabels.Gender(visibleGender),
            privacy.ShowSpoilers && real is not null && real != visibleGender ? SheetLabels.Gender(real) : null,
            traits,
            character.Traits.Count - traits.Count,
            visible,
            hiddenAppearances);
    }

    private sealed record Query(string? Text, IReadOnlyList<string> Traits, bool AnyTrait, string? GameId)
    {
        public static Query From(LibraryCharacterFilter? filter)
        {
            if (filter is null)
            {
                return new Query(null, [], false, null);
            }

            string? text = null;
            if (!string.IsNullOrWhiteSpace(filter.Text))
            {
                text = TitleSimilarity.Normalize(filter.Text);
                if (text.Length == 0)
                {
                    throw new LibraryException($"A busca '{filter.Text.Trim()}' não tem letras nem números.");
                }
            }

            var traits = new List<string>();
            foreach (var trait in filter.Traits ?? [])
            {
                var normalized = TitleSimilarity.Normalize(trait ?? string.Empty);
                traits.Add(normalized.Length == 0 ? throw new LibraryException($"O traço '{trait?.Trim()}' não tem letras nem números.") : normalized);
            }

            return new Query(text, traits, filter.AnyTrait, string.IsNullOrWhiteSpace(filter.GameId) ? null : filter.GameId.Trim());
        }

        public bool Matches(LibraryCharacterCard card)
        {
            if (GameId is not null && !card.AppearsIn.Any(a => string.Equals(a.GameId, GameId, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (Text is not null && !TextMatches(card))
            {
                return false;
            }

            if (Traits.Count == 0)
            {
                return true;
            }

            return AnyTrait ? Traits.Any(t => HasTrait(card, t)) : Traits.All(t => HasTrait(card, t));
        }

        private bool TextMatches(LibraryCharacterCard card) =>
            new[] { card.Name, card.Original }.Concat(card.Aliases).Concat(card.AppearsIn.Select(a => a.GameTitle))
                .OfType<string>()
                .Any(name => TitleSimilarity.Normalize(name).Contains(Text!, StringComparison.Ordinal));

        private static bool HasTrait(LibraryCharacterCard card, string wanted) =>
            card.Traits.Any(t => string.Equals(t.Id, wanted, StringComparison.OrdinalIgnoreCase)
                || TitleSimilarity.Normalize(t.Name).Contains(wanted, StringComparison.Ordinal)
                || TitleSimilarity.Normalize(t.OriginalName).Contains(wanted, StringComparison.Ordinal));
    }
}
