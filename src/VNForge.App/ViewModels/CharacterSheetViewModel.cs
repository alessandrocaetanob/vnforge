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

/// <summary>
/// A ficha de um personagem (#469) que o Glossário abre ao clicar no nome: gênero, como fala, cor, as falas mais longas no original e na tradução e
/// quantas linhas da IA esperam revisão. Lê a linha da tabela ao vivo (editar lá muda a ficha) e as falas do texto do projeto por <c>lookup</c>.
/// </summary>
public sealed partial class CharacterSheetViewModel : ObservableObject, IDisposable
{
    private readonly Func<string, SpeakerSheet?> lookup;
    private readonly Func<bool> textLoaded;
    private readonly Action<CharacterRow> review;
    private readonly Action close;

    public CharacterSheetViewModel(CharacterRow row, Func<string, SpeakerSheet?> lookup, Func<bool> textLoaded, Action<CharacterRow> review, Action close)
    {
        Row = row;
        this.lookup = lookup;
        this.textLoaded = textLoaded;
        this.review = review;
        this.close = close;
        row.PropertyChanged += OnRowChanged;
    }

    public CharacterRow Row { get; }

    /// <summary>O nome para mostrar: a tradução do cadastro quando há, senão o original.</summary>
    public string Name => !string.IsNullOrWhiteSpace(Row.Target) ? Row.Target.Trim() : Row.Source.Trim();

    /// <summary>O nome no jogo, como o texto o guarda; a ficha o mostra ao lado do nome traduzido.</summary>
    public string SourceName => Row.Source.Trim();

    public bool ShowsSourceName => SourceName.Length > 0 && !string.Equals(Name, SourceName, StringComparison.Ordinal);

    public string GenderLabel => ProjectLabels.Gender(Row.Gender);

    public string ToneText => string.IsNullOrWhiteSpace(Row.Tone) ? "Ainda sem descrição. Escreva em Como fala." : Row.Tone.Trim();

    public CharacterColor Color => Row.Color;

    public string UsesText => Row.Uses switch
    {
        null => "…",
        0 => "Nenhuma fala",
        1 => "1 fala",
        { } n => $"{ProjectLabels.Number(n)} falas",
    };

    /// <summary>As falas mais longas; vazio até o texto do projeto ser lido ou quando o personagem não fala.</summary>
    public IReadOnlyList<SheetLine> Lines =>
        lookup(Row.Source.Trim()) is { } sheet ? [.. sheet.Longest.Select(l => new SheetLine(l.Source, l.Translation))] : [];

    public bool HasLines => Lines.Count > 0;

    public string LinesEmptyText => textLoaded() ? "Este personagem ainda não fala em nenhuma linha do texto." : "Lendo o texto do projeto…";

    public int AwaitingReview => lookup(Row.Source.Trim())?.AwaitingReview ?? 0;

    public string AwaitingReviewText => !textLoaded()
        ? "Lendo o texto do projeto…"
        : AwaitingReview switch
        {
            0 => "Nenhuma linha da IA esperando revisão.",
            1 => "1 linha da IA esperando revisão.",
            var n => $"{ProjectLabels.Number(n)} linhas da IA esperando revisão.",
        };

    /// <summary>Abre o Modo revisão com as falas do personagem: o mesmo caminho do menu "Revisar falas deste personagem".</summary>
    [RelayCommand(CanExecute = nameof(CanReview))]
    private void Review() => review(Row);

    private bool CanReview() => AwaitingReview > 0;

    [RelayCommand]
    private void Close() => close();

    /// <summary>O texto do projeto foi lido de novo ou a linha mudou: tudo o que a ficha mostra é recalculado.</summary>
    public void Refresh()
    {
        OnPropertyChanged(string.Empty);
        ReviewCommand.NotifyCanExecuteChanged();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    public void Dispose() => Row.PropertyChanged -= OnRowChanged;
}
