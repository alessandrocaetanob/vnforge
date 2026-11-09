using VNForge.Core.Library;
using VNForge.Core.Text;

namespace VNForge.Core.Factsheet;

/// <summary>Quantos textos mudaram de etapa e quantos ficaram como estavam (sem tradução, ainda não revisados ou já na etapa).</summary>
public sealed record SheetMarkResult(int Changed, int Unchanged);

/// <summary>
/// Revisar, aprovar e corrigir à mão os textos da ficha de um jogo, como se faz com as falas. Todo método grava sob a trava de <see cref="SheetStore"/>, relendo o que muda.
/// Um nome de tag ou de traço vale para todos os jogos, então revisá-lo aqui o revisa em toda parte.
/// </summary>
public static class SheetEditor
{
    /// <summary>Todos os textos da ficha do jogo que existem: a sinopse, a descrição de cada personagem, o nome de cada tag e de cada traço.</summary>
    /// <exception cref="LibraryException">O jogo ainda não tem ficha.</exception>
    public static IReadOnlyList<(SheetField Field, SheetText Text)> Texts(SheetStore store, string visualNovelId)
    {
        ArgumentNullException.ThrowIfNull(store);
        var game = RequireGame(store, visualNovelId);
        var characters = store.LoadCharacters(game.CharacterIds);
        var terms = store.LoadTerms();
        var result = new List<(SheetField, SheetText)>();
        if (game.Synopsis is { } synopsis)
        {
            result.Add((SheetField.Synopsis, synopsis));
        }

        result.AddRange(characters.Where(c => c.Description is not null).Select(c => (SheetField.OfCharacter(c.Id), c.Description!)));
        foreach (var tag in game.Tags)
        {
            if (terms.Find(SheetTermKind.Tag, tag.Id) is { } term)
            {
                result.Add((SheetField.OfTag(tag.Id), term.Text));
            }
        }

        foreach (var id in characters.SelectMany(c => c.Traits).Select(t => t.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (terms.Find(SheetTermKind.Trait, id) is { } term)
            {
                result.Add((SheetField.OfTrait(id), term.Text));
            }
        }

        return result;
    }

    /// <summary>Corrige a tradução à mão. O texto vira <see cref="TranslationStatus.Translated"/>, que pede revisão de novo.</summary>
    /// <exception cref="LibraryException">O texto não é da ficha do jogo, o novo texto está vazio ou perde um trecho de spoiler.</exception>
    public static void Edit(SheetStore store, string visualNovelId, SheetField field, string target)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(field);
        store.Update(s => Apply(s, visualNovelId, field, text => text.EditedByPerson(target)));
    }

    /// <summary>
    /// Marca como revisados os textos pedidos, ou todos os que têm tradução ainda não revisada quando <paramref name="fields"/> é nulo. Em lote ficam de fora os
    /// <see cref="SheetText.Outdated"/>: revisar limpa o aviso, e o original mudou depois de a pessoa escrever. Esses só se revisam pelo nome.
    /// </summary>
    /// <exception cref="LibraryException">Um dos textos pedidos não é da ficha ou não tem tradução.</exception>
    public static SheetMarkResult Review(SheetStore store, string visualNovelId, IReadOnlyList<SheetField>? fields = null) =>
        Mark(store, visualNovelId, fields, text => text.Reviewed(), text => text.HasTarget && !text.Outdated && text.Status < TranslationStatus.Reviewed);

    /// <summary>Marca como aprovados os textos pedidos, ou todos os revisados quando <paramref name="fields"/> é nulo.</summary>
    /// <exception cref="LibraryException">Um dos textos pedidos não é da ficha ou ainda não foi revisado.</exception>
    public static SheetMarkResult Approve(SheetStore store, string visualNovelId, IReadOnlyList<SheetField>? fields = null) =>
        Mark(store, visualNovelId, fields, text => text.Approved(), text => text.Status == TranslationStatus.Reviewed);

    private static SheetMarkResult Mark(SheetStore store, string visualNovelId, IReadOnlyList<SheetField>? fields, Func<SheetText, SheetText> change, Func<SheetText, bool> eligible)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.Update(s =>
        {
            var all = Texts(s, visualNovelId);
            var targets = fields ?? [.. all.Where(t => eligible(t.Text)).Select(t => t.Field)];
            var changed = 0;
            foreach (var field in targets.Distinct())
            {
                Apply(s, visualNovelId, field, text =>
                {
                    var next = change(text);
                    changed += next == text ? 0 : 1;
                    return next;
                });
            }

            return new SheetMarkResult(changed, all.Count - changed);
        });
    }

    private static void Apply(SheetStore store, string visualNovelId, SheetField field, Func<SheetText, SheetText> change)
    {
        var game = RequireGame(store, visualNovelId);
        switch (field.Kind)
        {
            case SheetFieldKind.Synopsis:
                store.SaveGame(game with { Synopsis = change(game.Synopsis ?? throw NotInSheet(field)) });
                break;
            case SheetFieldKind.Character:
            {
                var character = game.CharacterIds.Contains(field.Id!) ? store.LoadCharacter(field.Id!) : null;
                var description = character?.Description ?? throw NotInSheet(field);
                store.SaveCharacter(character with { Description = change(description) });
                break;
            }

            case SheetFieldKind.Tag:
            {
                var kind = SheetTermKind.Tag;
                if (game.Tags.All(t => t.Id != field.Id))
                {
                    throw NotInSheet(field);
                }

                ChangeTerm(store, kind, field, change);
                break;
            }

            default:
            {
                if (!store.LoadCharacters(game.CharacterIds).Any(c => c.Traits.Any(t => t.Id == field.Id)))
                {
                    throw NotInSheet(field);
                }

                ChangeTerm(store, SheetTermKind.Trait, field, change);
                break;
            }
        }
    }

    private static void ChangeTerm(SheetStore store, SheetTermKind kind, SheetField field, Func<SheetText, SheetText> change)
    {
        var terms = store.LoadTerms();
        var term = terms.Find(kind, field.Id!) ?? throw NotInSheet(field);
        terms.Replace(kind, field.Id!, change(term.Text));
        store.SaveTerms(terms);
    }

    private static GameSheet RequireGame(SheetStore store, string visualNovelId) =>
        store.LoadGame(visualNovelId) ?? throw new LibraryException($"O jogo {visualNovelId} ainda não tem ficha. Busque-a com vnf library sheet fetch.");

    private static LibraryException NotInSheet(SheetField field) => new($"{field.Key} não está na ficha deste jogo.");
}
