using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.Core.Vndb;

namespace VNForge.App.ViewModels;

/// <summary>O pedido da ficha do jogo para abrir um personagem; <paramref name="RevealSpoilers"/> diz que a ficha estava com o spoiler no máximo.</summary>
public sealed record CharacterRequest(string Id, bool RevealSpoilers);

/// <summary>Uma linha do filtro da área Personagens (um papel ou um traço): o nome, quantos personagens ela pega e se está marcada.</summary>
public sealed partial class CharacterFacet(string key, string label, int count, string group = "") : ObservableObject
{
    /// <summary>O código do papel (<c>main</c>) ou o id do traço (<c>i34</c>).</summary>
    public string Key { get; } = key;

    public string Label { get; } = label;

    /// <summary>O grupo do VNDB do traço (Cabelo, Olhos); vazio no papel e no traço sem grupo.</summary>
    public string Group { get; } = group;

    /// <summary>O traço como o chip do filtro marcado o escreve: com o grupo ("Cabelo: Black"), porque dois traços de grupos diferentes podem ter o mesmo nome.</summary>
    public string ChipLabel => Group.Length > 0 ? $"{Group}: {Label}" : Label;

    public int Count { get; } = count;

    public string CountText => Count.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Os traços de um grupo do VNDB no filtro: o título da seção (Cabelo) e as linhas dela.</summary>
public sealed record CharacterFacetSection(string Title, IReadOnlyList<CharacterFacet> Facets)
{
    /// <summary>O título em maiúsculas, como o rótulo "TRAÇO" da barra.</summary>
    public string Heading => Title.ToUpper(CultureInfo.GetCultureInfo("pt-BR"));
}

/// <summary>
/// O retrato de conteúdo adulto no modo Desfocar. <paramref name="RevealKey"/> é o jogo do VNDB que o olho revela (o do papel mais importante do personagem); <paramref name="Revealed"/>
/// vale quando qualquer jogo dele foi revelado nesta sessão; <paramref name="IsBlurred"/> é o retrato borrado que ainda mostra o olho.
/// </summary>
public sealed record CharacterPortraitReveal(string RevealKey, bool Revealed, bool IsBlurred);

/// <summary>Um personagem da grade: retrato, nome, original, o papel mais importante dele e em quantos jogos da Biblioteca está.</summary>
public sealed record CharacterListItem(string Id, string Name, string? Original, VndbImageRef? Image, SheetPill Role, string GamesText, CharacterPortraitReveal Portrait)
{
    public bool HasOriginal => !string.IsNullOrEmpty(Original);

    public string AutomationName => $"{Name}. {Role.Text}. {GamesText}";
}

/// <summary>Um jogo da Biblioteca em que o personagem aparece: a capa, o título, o papel nele e de onde saiu esse papel.</summary>
public sealed record CharacterGameRow(string GameId, string Title, VndbImageRef? Cover, string Subtitle, double Progress, SheetPill Role, string Detail, bool Revealed = false)
{
    public bool HasCover => Cover is not null;

    /// <summary>A faixa de andamento da tradução no pé da capa, de 0 a 1; some enquanto o jogo não tem nada pronto.</summary>
    public bool HasProgress => Progress > 0;

    public string AutomationName => $"{Title}. {Role.Text}";
}

/// <summary>Os traços de um grupo do VNDB (Cabelo, Personalidade, Papel) na página do personagem.</summary>
public sealed record CharacterTraitGroup(string Name, IReadOnlyList<SheetChip> Chips);

/// <summary>O cartão "Na tradução": o que o projeto de tradução do jogo sabe do personagem (gênero do Glossário, nome no jogo e quantas falas ele tem).</summary>
public sealed record CharacterTranslation(string Gender, string GameName, string Lines)
{
    /// <summary>"Falas dela", "Falas dele" ou só "Falas", conforme o gênero do Glossário.</summary>
    public string LinesLabel => Gender switch { "Feminino" => "Falas dela", "Masculino" => "Falas dele", _ => "Falas" };
}

/// <summary>A página de um personagem, já com a privacidade aplicada pelo Core.</summary>
public sealed record CharacterPage(
    string Id,
    string Name,
    string? NamesLine,
    string? AliasesLine,
    VndbImageRef? Image,
    SheetPill Role,
    SheetPill DescriptionPill,
    string Description,
    IReadOnlyList<SheetChip> Tags,
    IReadOnlyList<CharacterTraitGroup> Groups,
    IReadOnlyList<CharacterGameRow> Games,
    string GamesCount,
    string? Note,
    CharacterPortraitReveal Portrait,
    CharacterTranslation? Translation = null)
{
    public bool HasNames => NamesLine is not null;

    public bool HasAliases => AliasesLine is not null;

    public bool HasGroups => Groups.Count > 0;

    public bool HasNote => Note is not null;

    public bool HasTranslation => Translation is not null;
}
