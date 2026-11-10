using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>A gaveta do jogo escolhido: o que o <see cref="LibraryDrawer"/> do Core diz, mais o que a tela precisa para ligar os botões.</summary>
/// <summary>Uma tag da gaveta como a tela a desenha: do VNDB, da pessoa, o "+N" ou o marcador de tag escondida (tracejado, com o olho riscado).</summary>
public sealed record LibraryDrawerChip(string Text, LibraryDrawerChipKind Kind)
{
    public bool Mine => Kind == LibraryDrawerChipKind.Mine;

    public bool IsHidden => Kind == LibraryDrawerChipKind.Hidden;

    public bool IsPlain => Kind != LibraryDrawerChipKind.Hidden;
}

public enum LibraryDrawerChipKind
{
    Vndb,
    Mine,
    More,
    Hidden,
}

public sealed class LibraryDrawerViewModel(LibraryCardViewModel card, LibraryDrawer drawer, LibraryViewModel library, string playedText = "")
{
    private bool japaneseLocale = card.Game.RequiresJapaneseLocale;

    public LibraryCardViewModel Card { get; } = card;

    public LibraryViewModel Library { get; } = library;

    /// <summary>O cartão "Na sua lista do VNDB"; nulo sem o serviço da lista ou com o jogo ainda sem confirmação.</summary>
    public LibraryVndbListViewModel? ListCard { get; } = library.ListCardFor(card.Game);

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

    /// <summary>As tags na ordem do quadro: as do VNDB, as da pessoa, o "+N" e, por último, o que a privacidade escondeu.</summary>
    public IReadOnlyList<LibraryDrawerChip> Chips { get; } = BuildChips(drawer);

    private static List<LibraryDrawerChip> BuildChips(LibraryDrawer drawer)
    {
        var chips = new List<LibraryDrawerChip>();
        chips.AddRange(drawer.Tags.Where(t => !t.Mine).Select(t => new LibraryDrawerChip(t.Text, LibraryDrawerChipKind.Vndb)));
        chips.AddRange(drawer.Tags.Where(t => t.Mine).Select(t => new LibraryDrawerChip(t.Text, LibraryDrawerChipKind.Mine)));
        if (drawer.MoreTags > 0)
        {
            chips.Add(new LibraryDrawerChip("+" + drawer.MoreTags, LibraryDrawerChipKind.More));
        }

        if (drawer.SpoilerTagsHidden)
        {
            chips.Add(new LibraryDrawerChip("Spoiler escondido", LibraryDrawerChipKind.Hidden));
        }

        if (drawer.AdultTagsHidden)
        {
            chips.Add(new LibraryDrawerChip("Adulto escondido", LibraryDrawerChipKind.Hidden));
        }

        return chips;
    }

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

    /// <summary>O tempo de jogo em uma linha, vindo do <c>playtime.json</c>.</summary>
    public string PlayedText { get; } = playedText;

    /// <summary>O jogo pede a localidade japonesa: o Jogar abre pelo Locale Emulator. Mudar grava no <c>library.json</c>.</summary>
    public bool RequiresJapaneseLocale
    {
        get => japaneseLocale;
        set
        {
            if (japaneseLocale != value)
            {
                japaneseLocale = value;
                Library.RememberJapaneseLocale(Card, value);
            }
        }
    }
}
