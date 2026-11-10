using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>A gaveta do jogo escolhido: o que o <see cref="LibraryDrawer"/> do Core diz, mais o que a tela precisa para ligar os botões.</summary>
public sealed class LibraryDrawerViewModel(LibraryCardViewModel card, LibraryDrawer drawer, LibraryViewModel library)
{
    public LibraryCardViewModel Card { get; } = card;

    public LibraryViewModel Library { get; } = library;

    public string Title => Card.Title;

    public string? OriginalTitle => Card.OriginalTitle;

    public bool HasOriginalTitle => Card.HasOriginalTitle;

    public string EngineText => drawer.EngineText;

    public string VndbText => drawer.VndbText;

    public string Duration => drawer.Duration;

    public string? ReleasedYear => drawer.ReleasedYear;

    public bool HasReleased => drawer.ReleasedYear is not null;

    public string? Synopsis => drawer.Synopsis;

    public bool HasSynopsis => !string.IsNullOrEmpty(drawer.Synopsis);

    public bool SynopsisReviewed => drawer.SynopsisReviewed;

    public IReadOnlyList<LibraryDrawerTag> Tags => drawer.Tags;

    public bool HasTags => drawer.Tags.Count > 0 || drawer.MoreTags > 0 || ShowSpoilerMarker || ShowAdultMarker;

    public bool HasMoreTags => drawer.MoreTags > 0;

    public string MoreTagsText => "+" + drawer.MoreTags;

    public bool ShowSpoilerMarker => drawer.SpoilerTagsHidden;

    public bool ShowAdultMarker => drawer.AdultTagsHidden;

    public bool HasTranslation => drawer.TranslationText is not null;

    public string TranslationText => drawer.TranslationText ?? string.Empty;

    /// <summary>A parte pronta, de 0 a 1, para a barra; a porcentagem do rótulo é a mesma do cartão da grade.</summary>
    public double TranslationProgress => Card.Progress;

    public string TranslationPercentText => (Card.Card.ProgressPercent ?? 0) + "%";

    /// <summary>O jogo tem candidatos do VNDB esperando confirmação: a gaveta oferece o botão que abre o diálogo.</summary>
    public bool NeedsConfirmation => Card.StateIsWarning;

    public bool IsMatched => Card.Game.IsConfirmed;

    public bool CanTranslate => Card.Game.Engine.Translatable;
}
