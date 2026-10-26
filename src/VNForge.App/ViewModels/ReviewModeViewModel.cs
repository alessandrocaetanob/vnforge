using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>O que a pessoa fez com uma linha da fila do modo revisão: nada ainda, aprovou, deixou para depois ou pulou.</summary>
public enum ReviewBlockState
{
    Pending,
    Approved,
    Later,
    Skipped,
}

/// <summary>O bloco de uma linha no topo do modo revisão ("Linha 1 de 6", um bloco por linha).</summary>
public sealed partial class ReviewBlock(int number) : ObservableObject
{
    public int Number { get; } = number;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsApproved), nameof(IsLater), nameof(IsSkipped))]
    public partial ReviewBlockState State { get; set; }

    public bool IsApproved => State == ReviewBlockState.Approved;

    public bool IsLater => State == ReviewBlockState.Later;

    public bool IsSkipped => State == ReviewBlockState.Skipped;

    /// <summary>É a linha em exibição; o bloco dela se destaca mesmo quando ela já foi aprovada e a pessoa voltou a vê-la.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}

/// <summary>
/// O modo revisão: uma tela para passar, uma a uma, pelas linhas da IA do script aberto e responder "esta fala está boa?". Usa a mesma lista de linhas
/// e o mesmo painel de detalhe do <see cref="EditorViewModel"/>: "Está boa" é o comando de aprovar do painel, então a gravação, o QA e o registro de
/// aprovações com hora são os do Editor. O modo guarda só a fila, a posição e o que a pessoa fez com cada linha.
/// </summary>
public sealed partial class ReviewModeViewModel : ObservableObject, IDisposable
{
    private readonly EditorViewModel editor;
    private readonly IReadOnlyList<LineViewModel> queue;
    private bool disposed;
    private bool navigating;

    /// <param name="editor">O Editor dono da lista de linhas e do painel de detalhe.</param>
    /// <param name="queue">As linhas a rever, na ordem do script.</param>
    public ReviewModeViewModel(EditorViewModel editor, IReadOnlyList<LineViewModel> queue)
    {
        this.editor = editor ?? throw new ArgumentNullException(nameof(editor));
        this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
        Blocks = [.. queue.Select((_, i) => new ReviewBlock(i + 1))];
        editor.PropertyChanged += OnEditorChanged;
        if (queue.Count == 0)
        {
            IsFinished = true;
            return;
        }

        Show(0);
    }

    public IReadOnlyList<ReviewBlock> Blocks { get; }

    /// <summary>A posição da linha em exibição na fila, de zero.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText), nameof(Previous), nameof(Next), nameof(PreviousLines), nameof(NextLines), nameof(HasPrevious), nameof(HasNext))]
    [NotifyPropertyChangedFor(nameof(SpeakerText))]
    public partial int Position { get; private set; }

    /// <summary>A fila acabou: a tela mostra o resumo e a próxima cena.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(FinishedTitle), nameof(FinishedText), nameof(NextSceneName), nameof(HasNextScene))]
    public partial bool IsFinished { get; private set; }

    public bool IsActive => !IsFinished;

    public int Total => queue.Count;

    /// <summary>Quantas linhas da fila estão aprovadas de fato, pelo status que o Editor gravou, e não pelo que a tela marcou.</summary>
    public int ApprovedCount => queue.Count(l => l.Status == TranslationStatus.Approved);

    public int LaterCount => Count(ReviewBlockState.Later);

    public int SkippedCount => Count(ReviewBlockState.Skipped);

    /// <summary>O painel de detalhe da linha em exibição; é o do Editor, trocado a cada linha.</summary>
    public LineDetailViewModel? Detail => editor.Detail;

    public string PositionText => Total == 0 ? "Nenhuma linha" : $"Linha {Math.Min(Position + 1, Total)} de {Total}";

    /// <summary>"Hoje: 12 linhas revisadas", do registro de aprovações; vazio enquanto nada foi aprovado hoje.</summary>
    public string TodayText => editor.ReviewedToday switch
    {
        0 => string.Empty,
        1 => "1 linha revisada hoje",
        var n => $"{ProjectLabels.Number(n)} linhas revisadas hoje",
    };

    public bool HasToday => editor.ReviewedToday > 0;

    public ContextLine? Previous => Neighbours.Previous;

    public ContextLine? Next => Neighbours.Next;

    /// <summary>A fala anterior numa lista de zero ou um item, para o <see cref="ContextLines"/>.</summary>
    public IReadOnlyList<ContextLine> PreviousLines => Previous is { } line ? [line] : [];

    public IReadOnlyList<ContextLine> NextLines => Next is { } line ? [line] : [];

    public bool HasPrevious => Previous is not null;

    public bool HasNext => Next is not null;

    /// <summary>Quem fala e como fala: o nome em português quando o personagem está cadastrado, seguido da voz ("美咲 → Misaki · seco e resmungão"); "Narração" sem falante.</summary>
    public string SpeakerText
    {
        get
        {
            if (Detail is not { } detail)
            {
                return string.Empty;
            }

            return detail.HasVoice ? $"{detail.SpeakerPair} · {detail.Voice}" : detail.SpeakerPair;
        }
    }

    /// <summary>O preço de "Outra sugestão" em reais; vazio quando o app não sabe estimar.</summary>
    public string SuggestionPriceText { get; private set; } = string.Empty;

    public bool HasSuggestionPrice => SuggestionPriceText.Length > 0;

    /// <summary>O personagem da linha não tem gênero definido: a fala pode sair com a concordância errada.</summary>
    public bool GenderMissing => Detail?.Context?.GenderMissing ?? false;

    public string FinishedTitle => Total == 0 ? "Nenhuma linha da IA para revisar" : "Revisão da cena concluída";

    public string FinishedText
    {
        get
        {
            if (Total == 0)
            {
                return "Esta cena não tem linhas traduzidas pela IA esperando revisão.";
            }

            var text = Plural(ApprovedCount, "linha aprovada", "linhas aprovadas");
            if (LaterCount > 0)
            {
                text += $", {Plural(LaterCount, "deixada para depois", "deixadas para depois")}";
            }

            if (SkippedCount > 0)
            {
                text += $" e {Plural(SkippedCount, "pulada", "puladas")}";
            }

            return text + ".";
        }
    }

    /// <summary>A próxima cena com linhas da IA para revisar; nulo na última.</summary>
    public string? NextSceneName => editor.NextScriptToReview()?.Display;

    public bool HasNextScene => NextSceneName is not null;

    private LineViewModel? Current => !IsFinished && Position < queue.Count ? queue[Position] : null;

    private (ContextLine? Previous, ContextLine? Next) Neighbours => Current is { } line ? editor.NeighboursOf(line) : (null, null);

    /// <summary>"Está boa" (Enter): aprova a linha e vai para a próxima. Se a gravação falha, fica na linha e o painel mostra o erro.</summary>
    [RelayCommand]
    private void Approve()
    {
        // O painel tem de ser o da linha em exibição: uma troca por fora (Ctrl+K, a paleta) que passasse despercebida aprovaria a linha errada.
        if (Detail is not { } detail || Current is not { } line || detail.Id != line.Id)
        {
            return;
        }

        detail.ApproveCommand.Execute(null);
        if (detail.Status == TranslationStatus.Approved)
        {
            Finish(ReviewBlockState.Approved);
        }
    }

    /// <summary>"Depois" (M): marca a linha para mais tarde e vai para a próxima, sem mexer na tradução.</summary>
    [RelayCommand]
    private void Later() => Finish(ReviewBlockState.Later);

    /// <summary>"Pular" (seta): vai para a próxima sem marcar nada.</summary>
    [RelayCommand]
    private void Skip() => Finish(ReviewBlockState.Skipped);

    /// <summary>Seta para trás: volta à linha de antes (ou à última, na tela final) para ver de novo.</summary>
    [RelayCommand]
    private void Back()
    {
        var target = IsFinished ? queue.Count - 1 : Position - 1;
        if (target < 0 || queue.Count == 0)
        {
            return;
        }

        IsFinished = false;
        Show(target);
    }

    /// <summary>"Corrigir" (E): sai do modo revisão com a linha aberta no campo da tradução do Editor.</summary>
    [RelayCommand]
    private void Correct() => editor.StopReview();

    [RelayCommand]
    private void Close() => editor.StopReview();

    /// <summary>"Definir o gênero": abre o Glossário nos personagens.</summary>
    [RelayCommand]
    private void DefineGender() => editor.RequestDefineGender();

    /// <summary>Na tela final: abre a próxima cena com linhas da IA e começa a revisá-la.</summary>
    [RelayCommand]
    private void GoToNextScene()
    {
        if (editor.NextScriptToReview() is { } next)
        {
            navigating = true;
            try
            {
                editor.OpenScript(next);
            }
            finally
            {
                navigating = false;
            }

            editor.StartReview();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        editor.PropertyChanged -= OnEditorChanged;
    }

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{ProjectLabels.Number(count)} {many}";

    private int Count(ReviewBlockState state) => Blocks.Count(b => b.State == state);

    /// <summary>Marca a linha em exibição e passa para a seguinte; depois da última, a fila acaba.</summary>
    private void Finish(ReviewBlockState state)
    {
        if (Current is null)
        {
            return;
        }

        var block = Blocks[Position];
        var before = block.State;

        // Uma linha já aprovada (a pessoa voltou a ela) só segue adiante: Depois e Pular não desfazem a aprovação.
        if (Current!.Status == TranslationStatus.Approved)
        {
            state = ReviewBlockState.Approved;
        }

        block.State = state;
        NotifyCounts();
        if (Position + 1 < queue.Count)
        {
            // Aprovada a linha já está gravada, haja o que houver; Depois e Pular só valem se a próxima linha abrir.
            if (!Show(Position + 1) && state != ReviewBlockState.Approved)
            {
                block.State = before;
                NotifyCounts();
            }
        }
        else
        {
            IsFinished = true;
            Blocks[Position].IsCurrent = false;
        }
    }

    private bool Show(int index)
    {
        navigating = true;
        bool shown;
        try
        {
            shown = editor.ShowLine(queue[index]);
        }
        finally
        {
            navigating = false;
        }

        if (!shown)
        {
            // A linha anterior não gravou: o painel dela mostra o erro e a pessoa continua nela.
            return false;
        }

        foreach (var block in Blocks.Where(b => b.IsCurrent))
        {
            block.IsCurrent = false;
        }

        Position = index;
        Blocks[index].IsCurrent = true;
        SuggestionPriceText = editor.SuggestionPrice is { } price ? price(queue[index].Entry.Unit) : string.Empty;

        OnPropertyChanged(nameof(Detail));
        NotifyLineChanged();
        return true;
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(ApprovedCount));
        OnPropertyChanged(nameof(LaterCount));
        OnPropertyChanged(nameof(SkippedCount));
        OnPropertyChanged(nameof(FinishedText));
    }

    private void NotifyLineChanged()
    {
        OnPropertyChanged(nameof(GenderMissing));
        OnPropertyChanged(nameof(SuggestionPriceText));
        OnPropertyChanged(nameof(HasSuggestionPrice));
    }

    /// <summary>
    /// Ctrl+K, "Traduzir pendentes" da paleta, os botões do Painel e a busca mudam a linha, o script ou o filtro do Editor por fora. A fila guarda as linhas do script de
    /// antes e a tela mostraria uma fala enquanto o Enter aprovaria outra, então o modo revisão fecha e o Editor fica onde a pessoa pediu.
    /// </summary>
    private void StopWhenNavigatedAway()
    {
        if (!navigating && !disposed)
        {
            editor.StopReview();
        }
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(EditorViewModel.Detail):
                OnPropertyChanged(nameof(Detail));
                OnPropertyChanged(nameof(SpeakerText));
                NotifyLineChanged();
                break;
            case nameof(EditorViewModel.SelectedLine):
            case nameof(EditorViewModel.SelectedScript):
            case nameof(EditorViewModel.Lines):
                StopWhenNavigatedAway();
                break;
            case nameof(EditorViewModel.ReviewedToday):
                OnPropertyChanged(nameof(TodayText));
                OnPropertyChanged(nameof(HasToday));
                break;
        }
    }
}
