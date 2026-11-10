using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
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

public sealed class LibraryDrawerViewModel(LibraryCardViewModel card, LibraryDrawer drawer, LibraryViewModel library, string playedText = "") : ObservableObject
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

    private LibraryVersionLabel? VersionLabel => Card.Version is { } version ? LibraryVersionLabel.For(version) : null;

    /// <summary>"Versão do jogo": só para o jogo que tem projeto ligado, que é de onde vêm o patch e o backup.</summary>
    public bool HasVersion => VersionLabel is not null;

    public string VersionPill => VersionLabel?.Pill ?? string.Empty;

    public string VersionTone => VersionLabel?.Tone ?? "pend";

    public string VersionDetail => VersionLabel?.Detail ?? string.Empty;

    public string? VersionNote => VersionLabel?.Note;

    public bool HasVersionNote => VersionNote is not null;

    /// <summary>"Última troca": quando a injeção que está na pasta foi gravada; só existe com patch no jogo.</summary>
    public string? LastSwitchText => Card.Version is { Kind: LibraryTranslationKind.Translated, Patch: { } patch }
        ? patch.AppliedAt.ToLocalTime().ToString("dd/MM/yyyy, HH:mm", CultureInfo.InvariantCulture)
        : null;

    public bool HasLastSwitch => LastSwitchText is not null;

    /// <summary>"Arquivos alterados": quantos a última injeção gravou e se todos têm cópia de segurança.</summary>
    public string? FilesText => Card.Version is { Kind: LibraryTranslationKind.Translated, FilesChanged: > 0 } version
        ? version.FilesWithoutOriginal == 0 ? $"{version.FilesChanged}, todos com cópia" : $"{version.FilesChanged}, {version.FilesWithoutOriginal} criados pelo patch"
        : null;

    public bool HasFiles => FilesText is not null;

    /// <summary>O Jogar abre o jogo como está na pasta; com a tradução inteira nela, o botão diz isso.</summary>
    public string PlayText => Card.Version is { } version && LibraryVersionLabel.IsFullTranslation(version) ? "Jogar traduzido" : "Jogar";

    public IReadOnlyList<LibraryVersionItem> Versions { get; } = [new(false, "Original"), new(true, "Traduzido")];

    /// <summary>Estado desconhecido não troca nada: o controle fica desligado e a gaveta diz o motivo.</summary>
    public bool IsVersionKnown => Card.Version is { } version && LibraryVersionLabel.IsKnown(version);

    /// <summary>O botão marcado: "Original" com o jogo como veio, "Traduzido" com a tradução inteira, nenhum quando a pasta só tem parte dela. Escolher o outro troca.</summary>
    public LibraryVersionItem? SelectedVersion
    {
        get => Card.Version switch
        {
            { Kind: LibraryTranslationKind.Original } => Versions[0],
            { } version when LibraryVersionLabel.IsFullTranslation(version) => Versions[1],
            _ => null,
        };
        set
        {
            if (value is not null && value != SelectedVersion && IsVersionKnown && !Library.IsSwitching)
            {
                Library.StartSwitch(Card, value.Translated);
            }

            // O controle já mostra o botão clicado: avisa para ele voltar ao que o estado diz, caso a troca não aconteça.
            OnPropertyChanged();
        }
    }

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

/// <summary>Um botão do controle "Original / Traduzido" da gaveta.</summary>
public sealed record LibraryVersionItem(bool Translated, string Label);
