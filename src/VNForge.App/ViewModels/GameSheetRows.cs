using VNForge.Core.Factsheet;
using VNForge.Core.Text;
using VNForge.Core.Vndb;

namespace VNForge.App.ViewModels;

/// <summary>A cor de um selo do quadro: as cinco etapas do texto mais o aviso, que as tags com spoiler usam.</summary>
public enum SheetTone
{
    Pending,
    Ai,
    Translated,
    Reviewed,
    Approved,
    Warning,
}

/// <summary>Um selo (.v3-pill) da ficha: o texto e a cor. As propriedades booleanas ligam as classes do estilo <c>sheet-pill</c>.</summary>
public sealed record SheetPill(string Text, SheetTone Tone)
{
    public bool IsPending => Tone == SheetTone.Pending;

    public bool IsAi => Tone == SheetTone.Ai;

    public bool IsTranslated => Tone == SheetTone.Translated;

    public bool IsReviewed => Tone == SheetTone.Reviewed;

    public bool IsApproved => Tone == SheetTone.Approved;

    public bool IsWarning => Tone == SheetTone.Warning;

    /// <summary>A cor da etapa: pendente cinza, IA roxo, traduzido azul, revisado azul-água e aprovado verde.</summary>
    public static SheetTone ToneOf(TranslationStatus status) => status switch
    {
        TranslationStatus.Machine => SheetTone.Ai,
        TranslationStatus.Translated => SheetTone.Translated,
        TranslationStatus.Reviewed => SheetTone.Reviewed,
        TranslationStatus.Approved => SheetTone.Approved,
        _ => SheetTone.Pending,
    };

    public static SheetPill Of(TranslationStatus status, bool feminine = false) => new(SheetLabels.StatusBadge(status, feminine), ToneOf(status));
}

/// <summary>Uma etiqueta (.v3-tag): tag do VNDB, tag sua (<paramref name="IsMine"/>), ou o aviso tracejado das que a privacidade escondeu (<paramref name="IsHidden"/>).</summary>
public sealed record SheetChip(string Text, bool IsMine = false, bool IsHidden = false, bool IsWarning = false)
{
    public bool IsPlain => !IsMine && !IsHidden && !IsWarning;
}

/// <summary>O que o cartão de personagem mostra no lugar do retrato.</summary>
public enum TileKind
{
    /// <summary>O retrato do VNDB.</summary>
    Portrait,

    /// <summary>O VNDB não tem imagem do personagem.</summary>
    NoImage,

    /// <summary>Há imagem, escondida por ser de conteúdo adulto.</summary>
    ImageHidden,

    /// <summary>O personagem inteiro está escondido por spoiler: o cartão fica sem nome e tem o "Mostrar".</summary>
    Hidden,
}

/// <summary>Um cartão de personagem da ficha (.ctile): retrato, nome, original, selos de papel e etapa e uma linha de traços ou de descrição.</summary>
public sealed record CharacterTile(
    string Id,
    TileKind Kind,
    string Name,
    string? Original,
    VndbImageRef? Image,
    SheetPill? Role,
    SheetPill? Status,
    string Line,
    string Caption,
    int HiddenLevel)
{
    public bool ShowsPortrait => Kind == TileKind.Portrait;

    public bool ShowsPlaceholder => Kind == TileKind.NoImage;

    public bool ShowsImageHidden => Kind == TileKind.ImageHidden;

    public bool ShowsHidden => Kind == TileKind.Hidden;

    /// <summary>A linha sob o nome: o original em japonês, ou o aviso do cartão sem imagem ou escondido.</summary>
    public bool HasOriginal => !string.IsNullOrEmpty(Original);

    public bool HasCaption => !HasOriginal && Caption.Length > 0;

    public bool HasLine => Line.Length > 0;

    public bool HasPills => Role is not null;

    public bool HasStatus => Status is not null;

    /// <summary>O nome da imagem para o leitor de tela.</summary>
    public string AutomationName => Kind == TileKind.Hidden ? "Personagem escondido por spoiler" : $"Cartão de {Name}";
}

/// <summary>Um nível do controle de spoiler (Escondido, Leve, Tudo).</summary>
public sealed record SpoilerOption(int Level, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Uma das três linhas do filtro da revisão (Tudo, Só IA, Só pendentes).</summary>
public sealed record SheetFilterOption(SheetReviewFilter Filter, string Label)
{
    public override string ToString() => Label;
}

public enum SheetReviewFilter
{
    All,
    Machine,
    Pending,
}
