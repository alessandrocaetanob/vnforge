using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.App.ViewModels;

/// <summary>Um jogo da grade ou da lista. Tudo o que a tela escreve vem pronto do <see cref="LibraryCard"/> do Core; aqui só moram a seleção e a escolha de ícone.</summary>
public sealed partial class LibraryCardViewModel(LibraryGame game, LibraryCard card, LibraryTranslationState? version = null, IReadOnlyList<LibraryDrawerChip>? tags = null, bool revealed = false) : ObservableObject
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

    /// <summary>O jogo é de conteúdo adulto: a capa leva o selo 18+ em qualquer modo.</summary>
    public bool IsAdult => Card.Adult;

    /// <summary>A capa está borrada: o modo é Desfocar e a pessoa ainda não pediu para ver este jogo. A capa leva o botão "Mostrar".</summary>
    public bool IsBlurred => Card.CoverBlurred && !Revealed;

    /// <summary>A pessoa já pediu para ver este jogo nesta sessão e o modo continua sendo Desfocar: a capa leva o olho que borra de novo.</summary>
    public bool CanConceal => Revealed && Card.Adult;

    /// <summary>A pessoa pediu para ver este jogo sem o borrão (só nesta sessão).</summary>
    public bool Revealed { get; } = revealed;

    public bool HasNoArt => Card.Cover == LibraryCoverKind.NoArt;

    public bool HasState => !IsBlurred && !CanConceal;

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

    /// <summary>O que a pasta do jogo tem agora (original, com o patch do VNForge ou desconhecido); nulo quando o jogo não tem projeto ligado.</summary>
    public LibraryTranslationState? Version { get; } = version;

    private LibraryVersionLabel? VersionLabel => Version is { } known && LibraryVersionLabel.IsKnown(known) ? LibraryVersionLabel.For(known) : null;

    /// <summary>A etiqueta sob o cartão: "PT-BR no jogo" ou "Original". Sem ela, o VNForge não afirma nada sobre a pasta.</summary>
    public bool HasVersionTag => VersionLabel is not null;

    public string VersionTag => VersionLabel?.Tag ?? string.Empty;

    public string VersionTone => VersionLabel?.Tone ?? "pend";

    public string AutomationName => $"{Title}. {Chip}. {Subtitle}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>O jogo está aberto agora, aberto por esta janela: a gaveta troca o Jogar por "Jogando".</summary>
    [ObservableProperty]
    public partial bool IsPlaying { get; set; }
}

/// <summary>Um jogo de "Continuar jogando", no topo da Biblioteca: o cartão dele e a linha "Hoje, 21:14 · 27 h 40 min".</summary>
public sealed record LibraryContinueViewModel(LibraryCardViewModel Card, string Detail)
{
    public string Title => Card.Title;

    public string AutomationName => $"{Card.Title}. {Detail}";
}
