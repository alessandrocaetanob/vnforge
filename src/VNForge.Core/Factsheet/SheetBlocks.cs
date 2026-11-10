using VNForge.Core.Library;
using VNForge.Core.Text;

namespace VNForge.Core.Factsheet;

/// <summary>O que um bloco da revisão da ficha é: a sinopse, os nomes das tags do jogo ou um personagem (descrição e traços).</summary>
public enum SheetBlockKind
{
    Synopsis,
    Tags,
    Character,
}

/// <summary>
/// Um texto ou uma lista de nomes da ficha, com o original do VNDB e a tradução lado a lado. Os nomes de tags e de traços formam uma lista (<paramref name="IsList"/>): o original e a
/// tradução saem separados por vírgula, na mesma ordem de <paramref name="Fields"/>. <paramref name="Texts"/> traz o texto de cada campo, na mesma ordem.
/// </summary>
public sealed record SheetPair(string Title, IReadOnlyList<SheetField> Fields, IReadOnlyList<SheetText> Texts, bool IsList)
{
    /// <summary>O original, com os marcadores <c>[spoiler]</c> como estão guardados; uma lista vai separada por vírgula.</summary>
    public string Original => string.Join(", ", Texts.Select(t => t.Source));

    /// <summary>A tradução, na mesma forma do original; nula quando nenhum texto tem tradução. Numa lista, o nome ainda sem tradução fica com o do VNDB.</summary>
    public string? Target => Texts.Any(t => t.HasTarget) ? string.Join(", ", Texts.Select(t => t.Shown)) : null;

    public bool HasTarget => Target is not null;

    /// <summary>A etapa mais atrasada entre os textos.</summary>
    public TranslationStatus Status => Texts.Min(t => t.Status);

    /// <summary>O VNDB mudou o original de algum texto depois de a tradução ser escrita.</summary>
    public bool Outdated => Texts.Any(t => t.Outdated);

    public bool HasSpoilers => SheetMarkup.HasSpoilers(Original);
}

/// <summary>Um bloco da revisão: o que se revisa de uma vez e passa à próxima. <paramref name="Status"/> é o do par mais atrasado.</summary>
public sealed record SheetBlock(SheetBlockKind Kind, string Key, string Title, string? CharacterId, IReadOnlyList<SheetPair> Pairs)
{
    public TranslationStatus Status => Pairs.Min(p => p.Status);

    public IReadOnlyList<SheetField> Fields => [.. Pairs.SelectMany(p => p.Fields).Distinct()];
}

/// <summary>
/// A revisão da ficha por blocos, como o quadro a desenha: a sinopse, os nomes das tags do jogo e, para cada personagem, a descrição e os nomes dos traços sem spoiler (os com spoiler só aparecem como etiqueta, no cartão "Traços com spoiler"). Os blocos seguem a
/// <see cref="SheetVisibility"/>: personagem, tag e traço escondidos por spoiler ou por conteúdo adulto não entram, e voltam quando a pessoa libera o nível.
/// </summary>
public static class SheetBlocks
{
    /// <summary>Os blocos da ficha do jogo, na ordem da tela. Sem sinopse, sem tags visíveis ou sem personagens, o bloco correspondente não existe.</summary>
    /// <exception cref="LibraryException">O jogo ainda não tem ficha.</exception>
    public static IReadOnlyList<SheetBlock> Build(SheetStore store, string visualNovelId, SheetVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(store);
        var game = store.LoadGame(visualNovelId) ?? throw new LibraryException($"O jogo {visualNovelId} ainda não tem ficha. Busque-a com vnf library sheet fetch.");
        return Build(game, store.LoadCharacters(game.CharacterIds), store.LoadTerms(), visibility);
    }

    /// <inheritdoc cref="Build(SheetStore, string, SheetVisibility)"/>
    public static IReadOnlyList<SheetBlock> Build(GameSheet game, IEnumerable<CharacterSheet> characters, SheetTerms terms, SheetVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(visibility);
        var blocks = new List<SheetBlock>();
        if (game.Synopsis is { } synopsis)
        {
            blocks.Add(new SheetBlock(SheetBlockKind.Synopsis, SheetField.Synopsis.Key, "Sinopse", null, [new SheetPair("Sinopse", [SheetField.Synopsis], [synopsis], false)]));
        }

        var tags = game.Tags.Where(t => !SheetViewer.IsTagHidden(t, visibility)).Select(t => (Field: SheetField.OfTag(t.Id), t.Name, Term: terms.Find(SheetTermKind.Tag, t.Id))).ToList();
        if (tags.Count > 0)
        {
            blocks.Add(new SheetBlock(SheetBlockKind.Tags, "tags", "Gêneros e tags", null, [List("Gêneros e tags", tags)]));
        }

        var byId = characters.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var character in game.CharacterIds.Select(id => byId.GetValueOrDefault(id)).OfType<CharacterSheet>())
        {
            if ((character.In(game.VisualNovelId)?.Spoiler ?? 0) > visibility.SpoilerLevel)
            {
                continue;
            }

            var pairs = new List<SheetPair>();
            if (character.Description is { } description)
            {
                pairs.Add(new SheetPair($"Descrição de {character.Name}", [SheetField.OfCharacter(character.Id)], [description], false));
            }

            var traits = character.Traits.Where(t => t.Spoiler == 0 && !t.Lie && !SheetViewer.IsTraitHidden(t, visibility)).Select(t => (Field: SheetField.OfTrait(t.Id), t.Name, Term: terms.Find(SheetTermKind.Trait, t.Id))).ToList();
            if (traits.Count > 0)
            {
                pairs.Add(List($"Traços de {character.Name}", traits));
            }

            if (pairs.Count > 0)
            {
                blocks.Add(new SheetBlock(SheetBlockKind.Character, SheetField.OfCharacter(character.Id).Key, character.Name, character.Id, pairs));
            }
        }

        return blocks;
    }

    /// <summary>
    /// Grava a tradução escrita à mão. Um texto só vai inteiro; uma lista (tags, traços) precisa de um nome por item do original, separados por vírgula e na mesma ordem. O que muda vira
    /// <see cref="TranslationStatus.Translated"/>, que pede revisão de novo; o nome que ficou igual ao que já estava não muda de etapa.
    /// </summary>
    /// <exception cref="LibraryException">A lista não tem um nome por item, o texto está vazio ou perde um trecho de spoiler.</exception>
    public static void EditPair(SheetStore store, string visualNovelId, SheetPair pair, string target)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(pair);
        if (!pair.IsList)
        {
            SheetEditor.Edit(store, visualNovelId, pair.Fields[0], target);
            return;
        }

        var names = (target ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Length != pair.Fields.Count)
        {
            throw new LibraryException($"Escreva {pair.Fields.Count} {(pair.Fields.Count == 1 ? "nome" : "nomes")} separados por vírgula, na mesma ordem do original ({names.Length} escritos).");
        }

        for (var i = 0; i < names.Length; i++)
        {
            if (pair.Texts[i] is { HasTarget: true } text && string.Equals(text.Target, names[i], StringComparison.Ordinal))
            {
                continue;
            }

            SheetEditor.Edit(store, visualNovelId, pair.Fields[i], names[i]);
        }
    }

    private static SheetPair List(string title, IReadOnlyList<(SheetField Field, string Name, SheetTerm? Term)> items) =>
        new(title, [.. items.Select(i => i.Field)], [.. items.Select(i => i.Term?.Text ?? SheetText.Pending(i.Name))], true);
}
