using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>
/// O leitor de cena (#475, "Ler como jogador"): a cena do script aberto em tela cheia, uma fala por vez na caixa do jogo, como quem joga. Espaço avança, a seta volta,
/// <c>F</c> marca "soa estranho" (<see cref="ReadingFlags.SoundsOdd"/>). Não aprova, não muda status nem pede nada à pessoa: ritmo, palavra repetida entre falas e a
/// voz que muda no meio da cena aparecem na leitura corrida, não na linha a linha. As falas são as do Editor; a marca é gravada pelo <see cref="EditorViewModel"/>.
/// </summary>
public sealed partial class ReadSceneViewModel : ObservableObject, IDisposable
{
    private readonly EditorViewModel editor;
    private readonly IReadOnlyList<LineViewModel> lines;
    private readonly HashSet<string> markedNow = new(StringComparer.Ordinal);
    private bool disposed;

    /// <param name="editor">O Editor dono das linhas e da gravação.</param>
    /// <param name="lines">As falas da cena, na ordem do script.</param>
    /// <param name="start">A fala em que a leitura começa, de zero.</param>
    public ReadSceneViewModel(EditorViewModel editor, IReadOnlyList<LineViewModel> lines, int start = 0)
    {
        this.editor = editor ?? throw new ArgumentNullException(nameof(editor));
        this.lines = lines ?? throw new ArgumentNullException(nameof(lines));
        Position = Math.Clamp(start, 0, Math.Max(0, lines.Count - 1));
        Line = Current is { } line ? editor.ReadingLineOf(line) : null;
        editor.PropertyChanged += OnEditorChanged;
    }

    /// <summary>A posição da fala em exibição, de zero.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(PositionText), nameof(SpeakerText), nameof(IsOdd), nameof(CanGoBack), nameof(IsLast))]
    public partial int Position { get; private set; }

    /// <summary>Passou da última fala: a tela mostra o fim da cena e o que foi marcado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(FinishedText), nameof(CanGoBack))]
    public partial bool IsFinished { get; private set; }

    /// <summary>A fala em exibição como a caixa a desenha; nula quando a cena não tem fala.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Layout), nameof(Problem), nameof(HasProblem), nameof(ShowsSource), nameof(HasLayout))]
    public partial ReadingLine? Line { get; private set; }

    /// <summary>A falha da última marcação, em português; nula quando deu certo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;

    public bool IsActive => !IsFinished && lines.Count > 0;

    public int Total => lines.Count;

    public string SceneName => editor.SelectedScript?.Name ?? string.Empty;

    public TextboxLayout? Layout => Line?.Layout;

    public bool HasLayout => Layout is not null;

    public string? Problem => Line?.Problem;

    public bool HasProblem => Problem is not null;

    /// <summary>A fala não tem tradução e a caixa mostra o original.</summary>
    public bool ShowsSource => Line?.ShowsSource ?? false;

    public LineViewModel? Current => !IsFinished && Position < lines.Count ? lines[Position] : null;

    public string PositionText => lines.Count == 0 ? "Nenhuma fala" : $"Fala {ProjectLabels.Number(Math.Min(Position + 1, Total))} de {ProjectLabels.Number(Total)}";

    /// <summary>Quem fala, como o jogo mostra (o nome traduzido do Glossário, ou "Narração"); vazio no fim.</summary>
    public string SpeakerText => Current?.SpeakerLabel ?? string.Empty;

    /// <summary>A fala em exibição está marcada como "soa estranho".</summary>
    public bool IsOdd => Current?.Entry.Unit.IsOdd() ?? false;

    public bool CanGoBack => IsFinished || Position > 0;

    public bool IsLast => Position >= lines.Count - 1;

    /// <summary>Quantas falas da cena estão marcadas agora, contando as de leituras anteriores.</summary>
    public int OddCount => lines.Count(l => l.Entry.Unit.IsOdd());

    /// <summary>Alguma fala foi marcada, e continua marcada, nesta leitura; é o que manda o Editor abrir com o filtro ao sair.</summary>
    public bool HasMarkedNow => markedNow.Count > 0;

    public string OddText => OddCount switch
    {
        0 => string.Empty,
        1 => "1 fala soa estranho",
        var n => $"{ProjectLabels.Number(n)} falas soam estranho",
    };

    public bool HasOdd => OddCount > 0;

    /// <summary>O resumo do fim da cena: quantas falas estão marcadas e, se alguma foi marcada agora, que o Editor abre com o filtro delas.</summary>
    public string FinishedText => OddCount switch
    {
        0 => "Nenhuma fala marcada como estranha.",
        1 => HasMarkedNow ? "1 fala marcada como estranha. Ao sair, o Editor abre só com ela." : "1 fala marcada como estranha.",
        var n => HasMarkedNow
            ? $"{ProjectLabels.Number(n)} falas marcadas como estranhas. Ao sair, o Editor abre só com elas."
            : $"{ProjectLabels.Number(n)} falas marcadas como estranhas.",
    };

    /// <summary>Espaço: mostra a fala seguinte; depois da última, o fim da cena.</summary>
    [RelayCommand]
    private void Next()
    {
        if (IsFinished || lines.Count == 0)
        {
            return;
        }

        if (IsLast)
        {
            IsFinished = true;
            Error = null;
            NotifyMarks();
            return;
        }

        Show(Position + 1);
    }

    /// <summary>A seta para a esquerda: volta à fala anterior; do fim da cena, à última fala.</summary>
    [RelayCommand]
    private void Back()
    {
        if (IsFinished)
        {
            IsFinished = false;
            Show(Position);
            return;
        }

        if (Position > 0)
        {
            Show(Position - 1);
        }
    }

    /// <summary><c>F</c>: marca a fala como "soa estranho", ou tira a marca se já estava. A marca vai para o arquivo na hora.</summary>
    [RelayCommand]
    private void ToggleOdd()
    {
        if (Current is not { } line)
        {
            return;
        }

        var odd = !line.Entry.Unit.IsOdd();
        Error = editor.SetOdd(line, odd);
        if (Error is not null)
        {
            return;
        }

        if (odd)
        {
            markedNow.Add(line.Id);
        }
        else
        {
            markedNow.Remove(line.Id);
        }

        OnPropertyChanged(nameof(IsOdd));
        NotifyMarks();
    }

    /// <summary>Esc: sai do leitor. Com falas marcadas nesta leitura, o Editor abre com o filtro delas.</summary>
    [RelayCommand]
    private void Close() => editor.StopReading();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        editor.PropertyChanged -= OnEditorChanged;
    }

    private void Show(int position)
    {
        Error = null;
        Position = position;
        Line = Current is { } line ? editor.ReadingLineOf(line) : null;
    }

    private void NotifyMarks()
    {
        OnPropertyChanged(nameof(OddCount));
        OnPropertyChanged(nameof(OddText));
        OnPropertyChanged(nameof(HasOdd));
        OnPropertyChanged(nameof(FinishedText));
    }

    /// <summary>Outra cena aberta deixa as falas do leitor sem dono: ele fecha, sem mexer no filtro.</summary>
    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.SelectedScript) && ReferenceEquals(editor.Reading, this))
        {
            editor.EndReading();
        }
    }
}
