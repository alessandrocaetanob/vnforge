using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>A gaveta do jogo escolhido: o que o <see cref="LibraryDrawer"/> do Core diz, mais o que a tela precisa para ligar os botões.</summary>
/// <summary>Uma tag da gaveta como a tela a desenha: do VNDB, da pessoa, o "+N" ou o marcador de tag escondida (tracejado, com o olho riscado).</summary>
public sealed record LibraryDrawerChip(string Text, LibraryDrawerChipKind Kind)
{
    public bool Mine => Kind == LibraryDrawerChipKind.Mine;

    public bool IsHidden => Kind == LibraryDrawerChipKind.Hidden;

    public bool IsPlain => Kind != LibraryDrawerChipKind.Hidden;

    /// <summary>O chip "+ tag" que abre a escolha das suas tags.</summary>
    public bool IsAdd => Kind == LibraryDrawerChipKind.Add;

    /// <summary>Uma tag de verdade: nem o marcador de escondida nem o "+ tag".</summary>
    public bool IsTag => Kind is LibraryDrawerChipKind.Vndb or LibraryDrawerChipKind.Mine or LibraryDrawerChipKind.More;
}

public enum LibraryDrawerChipKind
{
    Vndb,
    Mine,
    More,
    Hidden,
    Add,
}

public sealed partial class LibraryDrawerViewModel(LibraryCardViewModel card, LibraryDrawer drawer, LibraryViewModel library, string playedText = "") : ObservableObject
{
    private bool japaneseLocale = card.Game.RequiresJapaneseLocale;

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

        chips.Add(new LibraryDrawerChip("tag", LibraryDrawerChipKind.Add));
        return chips;
    }

    /// <summary>As suas tags (todas as do registro) com a caixa marcada nas que este jogo tem, para o "+ tag".</summary>
    public ObservableCollection<LibraryTagOptionViewModel> PickerOptions { get; } = [];

    [ObservableProperty]
    public partial bool IsPickerOpen { get; set; }

    [ObservableProperty]
    public partial string NewTagName { get; set; } = string.Empty;

    public bool HasNoOwnTags => PickerOptions.Count == 0;

    partial void OnIsPickerOpenChanged(bool value)
    {
        if (value)
        {
            NewTagName = string.Empty;
            LoadPicker();
        }
    }

    private void LoadPicker()
    {
        PickerOptions.Clear();
        var tags = Library.UserTags;
        foreach (var tag in tags.Tags)
        {
            PickerOptions.Add(new LibraryTagOptionViewModel(tag.Name, tag.Name, tags.GamesWith(tag.Name).Count, true, tags.Has(Card.Id, tag.Name)));
        }

        OnPropertyChanged(nameof(HasNoOwnTags));
    }

    [RelayCommand]
    private void TogglePicker() => IsPickerOpen = !IsPickerOpen;

    /// <summary>Marca ou desmarca a tag no jogo; a gaveta se refaz e o escolhedor continua aberto para a próxima.</summary>
    [RelayCommand]
    private void ToggleTag(LibraryTagOptionViewModel? option)
    {
        if (option is not null)
        {
            Apply(option.Text, !option.IsChecked);
        }
    }

    /// <summary>O x do chip de uma tag sua.</summary>
    [RelayCommand]
    private void RemoveTag(LibraryDrawerChip? chip)
    {
        if (chip is { Mine: true })
        {
            Apply(chip.Text, false);
        }
    }

    /// <summary>Cria a tag com o nome digitado e já a põe no jogo.</summary>
    [RelayCommand]
    private void CreateTag()
    {
        if (!string.IsNullOrWhiteSpace(NewTagName))
        {
            Apply(NewTagName, true);
        }
    }

    private void Apply(string name, bool on)
    {
        var open = IsPickerOpen;
        if (Library.SetGameTag(Card.Id, name, on) && Library.Drawer is { } fresh)
        {
            fresh.IsPickerOpen = open;
        }
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
