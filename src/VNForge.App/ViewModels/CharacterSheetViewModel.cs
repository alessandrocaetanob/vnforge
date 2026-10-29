using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core.Glossary;

namespace VNForge.App.ViewModels;

/// <summary>Uma fala da ficha: o original e a tradução, ou o aviso de que ainda não há tradução.</summary>
public sealed record SheetLine(string Source, string? Translation)
{
    public bool HasTranslation => !string.IsNullOrWhiteSpace(Translation);

    public string TranslationText => HasTranslation ? Translation! : "Ainda sem tradução";
}

/// <summary>Em que ponto está a leitura do texto do projeto que alimenta a ficha.</summary>
public enum SheetLoadState
{
    Loading,
    Ready,
    Failed,
}

/// <summary>
/// A ficha de um personagem (#469) que o Glossário abre pelo ícone da linha: gênero, como fala, cor, as falas mais longas no original e na tradução e
/// quantas linhas da IA esperam revisão. Lê a linha da tabela ao vivo (editar lá muda a ficha); as falas vêm de uma leitura do texto feita a cada abertura
/// (<see cref="Apply"/>), porque o status das linhas muda no Editor sem o Glossário saber.
/// </summary>
public sealed partial class CharacterSheetViewModel : ObservableObject, IDisposable
{
    private readonly Action<CharacterRow> review;
    private readonly Action close;
    private IReadOnlyDictionary<string, SpeakerSheet>? sheets;
    private SpeakerSheet? current;
    private IReadOnlyList<SheetLine> lines = [];

    public CharacterSheetViewModel(CharacterRow row, Action<CharacterRow> review, Action close)
    {
        Row = row;
        this.review = review;
        this.close = close;
        row.PropertyChanged += OnRowChanged;
    }

    public CharacterRow Row { get; }

    public SheetLoadState State { get; private set; } = SheetLoadState.Loading;

    /// <summary>O nome para mostrar: a tradução do cadastro quando há, senão o original.</summary>
    public string Name => !string.IsNullOrWhiteSpace(Row.Target) ? Row.Target.Trim() : Row.Source.Trim();

    /// <summary>Sem tradução cadastrada o título é o nome japonês, que pede a fonte do japonês.</summary>
    public bool NameIsJapanese => string.IsNullOrWhiteSpace(Row.Target);

    /// <summary>O nome no jogo, como o texto o guarda; a ficha o mostra ao lado do nome traduzido.</summary>
    public string SourceName => Row.Source.Trim();

    public bool ShowsSourceName => SourceName.Length > 0 && !string.Equals(Name, SourceName, StringComparison.Ordinal);

    public string GenderLabel => ProjectLabels.Gender(Row.Gender);

    public string ToneText => string.IsNullOrWhiteSpace(Row.Tone) ? "Ainda sem descrição. Escreva em Como fala." : Row.Tone.Trim();

    public CharacterColor Color => Row.Color;

    public string UsesText => State switch
    {
        SheetLoadState.Loading => "…",
        SheetLoadState.Failed => "Não lido",
        _ => (current?.Total ?? 0) switch
        {
            0 => "Nenhuma fala",
            1 => "1 fala",
            var n => $"{ProjectLabels.Number(n)} falas",
        },
    };

    /// <summary>As falas mais longas; vazio até o texto do projeto ser lido ou quando o personagem não fala.</summary>
    public IReadOnlyList<SheetLine> Lines => lines;

    public bool HasLines => lines.Count > 0;

    public string LinesEmptyText => State switch
    {
        SheetLoadState.Loading => "Lendo o texto do projeto…",
        SheetLoadState.Failed => "Não foi possível ler o texto do projeto.",
        _ => "Este personagem ainda não fala em nenhuma linha do texto.",
    };

    public int AwaitingReview => current?.AwaitingReview ?? 0;

    public string AwaitingReviewText => State switch
    {
        SheetLoadState.Loading => "Lendo o texto do projeto…",
        SheetLoadState.Failed => "Não foi possível contar as linhas esperando revisão.",
        _ => AwaitingReview switch
        {
            0 => "Nenhuma linha da IA esperando revisão.",
            1 => "1 linha da IA esperando revisão.",
            var n => $"{ProjectLabels.Number(n)} linhas da IA esperando revisão.",
        },
    };

    /// <summary>Abre o Modo revisão com as falas do personagem: o mesmo caminho do menu "Revisar falas deste personagem".</summary>
    [RelayCommand(CanExecute = nameof(CanReview))]
    private void Review() => review(Row);

    private bool CanReview() => State == SheetLoadState.Ready && AwaitingReview > 0;

    [RelayCommand]
    private void Close() => close();

    /// <summary>O texto do projeto foi lido: as falas e as contagens passam a valer.</summary>
    public void Apply(IReadOnlyDictionary<string, SpeakerSheet> all)
    {
        sheets = all;
        State = SheetLoadState.Ready;
        Lookup();
        RaiseAll();
    }

    /// <summary>O texto do projeto não pôde ser lido: a ficha avisa em vez de dizer que o personagem não fala.</summary>
    public void Fail()
    {
        sheets = null;
        State = SheetLoadState.Failed;
        Lookup();
        RaiseAll();
    }

    private void Lookup()
    {
        current = sheets is not null && sheets.TryGetValue(Row.Source.Trim(), out var sheet) ? sheet : null;
        lines = current is null ? [] : [.. current.Longest.Select(l => new SheetLine(l.Source, l.Translation))];
    }

    private void RaiseAll()
    {
        OnPropertyChanged(string.Empty);
        ReviewCommand.NotifyCanExecuteChanged();
    }

    // As falas só se refazem quando o nome no jogo muda: o resto da linha só troca texto curto.
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CharacterRow.Source):
                Lookup();
                RaiseAll();
                break;
            case nameof(CharacterRow.Target):
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(NameIsJapanese));
                OnPropertyChanged(nameof(ShowsSourceName));
                break;
            case nameof(CharacterRow.Gender):
                OnPropertyChanged(nameof(GenderLabel));
                break;
            case nameof(CharacterRow.Tone):
                OnPropertyChanged(nameof(ToneText));
                break;
            case nameof(CharacterRow.Color):
                OnPropertyChanged(nameof(Color));
                break;
        }
    }

    public void Dispose() => Row.PropertyChanged -= OnRowChanged;
}
