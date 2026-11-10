using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.App.ViewModels;

/// <summary>Um jogo da grade ou da lista. Tudo o que a tela escreve vem pronto do <see cref="LibraryCard"/> do Core; aqui só moram a seleção e a escolha de ícone.</summary>
public sealed partial class LibraryCardViewModel(LibraryGame game, LibraryCard card, IReadOnlyList<LibraryDrawerChip>? tags = null) : ObservableObject
{
    public LibraryGame Game { get; } = game;

    public LibraryCard Card { get; } = card;

    public string Id => Card.Id;

    public string Title => Card.Title;

    public string? OriginalTitle => Card.OriginalTitle;

    public bool HasOriginalTitle => !string.IsNullOrEmpty(Card.OriginalTitle);

    public string Subtitle => Card.Subtitle;

    public string Chip => Card.Chip;

    public bool ChipWarn => Card.ChipWarn;

    public VndbImageRef? Image => Card.Image;

    public bool HasImage => Card.Cover == LibraryCoverKind.Image;

    public bool IsAdultVeil => Card.Cover == LibraryCoverKind.AdultVeil;

    public bool HasNoArt => Card.Cover == LibraryCoverKind.NoArt;

    public bool HasState => Card.Match != LibraryMatchState.None;

    public bool StateIsWarning => Card.Match == LibraryMatchState.NeedsConfirmation;

    public string StateText => Card.MatchText;

    /// <summary>A chave do ícone do círculo no canto da capa: o visto do casamento, o triângulo de aviso e o elo de quem não casou.</summary>
    public string StateIcon => Card.Match switch
    {
        LibraryMatchState.Matched => "Icon.Check",
        LibraryMatchState.NeedsConfirmation => "Icon.Warning",
        _ => "Icon.Link",
    };

    /// <summary>A faixa de andamento no pé da capa, de 0 a 1; some enquanto não há nada pronto.</summary>
    public double Progress => (Card.ProgressPercent ?? 0) / 100.0;

    public bool HasProgress => Card.ProgressPercent is > 0;

    /// <summary>As tags que o filtro por tag procura neste jogo; vazio sem filtro.</summary>
    public IReadOnlyList<LibraryDrawerChip> Tags { get; } = tags ?? [];

    public bool HasTags => Tags.Count > 0;

    public string AutomationName => $"{Title}. {Chip}. {Subtitle}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
