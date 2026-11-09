namespace VNForge.Core.Factsheet;

/// <summary>O que um item a traduzir é: o texto corrido (sinopse, descrição de personagem) ou um nome curto (tag, traço).</summary>
public enum SheetItemKind
{
    Synopsis,
    CharacterDescription,
    TagName,
    TraitName,
}

/// <summary>
/// Um item a traduzir. <paramref name="Key"/> identifica o item na resposta (<c>synopsis</c>, <c>character:c12</c>, <c>tag:g7</c>, <c>trait:i34</c>) e volta igual.
/// <paramref name="Context"/> ajuda a escolher o sentido e não se traduz: o nome do personagem, a categoria da tag, o grupo do traço.
/// </summary>
public sealed record SheetTranslationItem(string Key, SheetItemKind Kind, string Source, string? Context)
{
    /// <summary>Texto corrido, que pode trazer trechos <c>[spoiler]...[/spoiler]</c> que a tradução mantém.</summary>
    public bool IsProse => Kind is SheetItemKind.Synopsis or SheetItemKind.CharacterDescription;
}

/// <summary>Um pedido: os itens de um lote e o título do jogo, que situa a IA. Itens de tipos diferentes podem vir no mesmo pedido.</summary>
public sealed record SheetTranslationRequest(string GameTitle, IReadOnlyList<SheetTranslationItem> Items);

/// <summary>A tradução de um item, pela mesma <see cref="SheetTranslationItem.Key"/>.</summary>
public sealed record SheetTranslation(string Key, string Text);

/// <summary>
/// Fronteira entre a ficha e a IA, no mesmo molde das outras: o Core não conhece o provedor. O provedor Claude (<c>VNForge.AI</c>) e o <c>FakeSheetTranslator</c> dos
/// testes a implementam. Traduz do inglês (a língua do VNDB) para o português do Brasil, e devolve um item por item pedido; quem chama confere chaves e marcadores de spoiler.
/// </summary>
public interface ISheetTranslator
{
    Task<IReadOnlyList<SheetTranslation>> TranslateAsync(SheetTranslationRequest request, CancellationToken ct);
}
