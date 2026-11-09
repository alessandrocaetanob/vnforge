using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Claude;
using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>
/// A sugestão da segunda passada de editor (#532) para a linha aberta. Nada muda na tradução até "Aceitar"; "Ignorar" só tira a sugestão. Ela vale enquanto a linha gravada
/// tem a tradução em que a IA se baseou: qualquer edição que grave outro texto a aposenta.
/// </summary>
public sealed partial class LineDetailViewModel
{
    private EditorSuggestion? editorSuggestion;
    private int editorPosition;
    private int editorTotal;
    private CancellationTokenSource? anotherCancellation;

    /// <summary>Avisa o Editor que a sugestão foi aceita ou ignorada, para ele tirá-la do arquivo e da grade.</summary>
    public Action<EditorSuggestion>? EditorSuggestionResolved { get; set; }

    /// <summary>Pede ao editor outra proposta para a linha ("Outra"); sem ele o botão fica desligado. Devolve nulo quando o modelo não vê outra forma melhor.</summary>
    public Func<EditorSuggestion, CancellationToken, Task<EditorSuggestion?>>? EditorSuggestionAnother { get; set; }

    /// <summary>Avisa o Editor que "Outra" trouxe uma proposta nova, para a grade e a lista de sugestões trocarem a antiga.</summary>
    public Action<EditorSuggestion>? EditorSuggestionReplaced { get; set; }

    /// <summary>"Outra" está esperando a resposta do modelo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorSuggestionOtherText))]
    [NotifyCanExecuteChangedFor(nameof(OtherEditorSuggestionCommand))]
    public partial bool IsAskingAnotherEditorSuggestion { get; private set; }

    /// <summary>O resultado de "Outra" que não virou proposta nova: o modelo não achou outra forma, ou o pedido falhou. Vazio quando não há o que dizer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorSuggestionNote))]
    public partial string EditorSuggestionNote { get; private set; } = string.Empty;

    public bool HasEditorSuggestionNote => EditorSuggestionNote.Length > 0;

    /// <summary>O texto do botão "Outra".</summary>
    public string EditorSuggestionOtherText => IsAskingAnotherEditorSuggestion ? "Pedindo..." : "Outra";

    /// <summary>Mostra a sugestão da passada de editor, que é a <paramref name="position"/>ª das <paramref name="total"/> da cena; nula esconde o cartão.</summary>
    public void ShowEditorSuggestion(EditorSuggestion? suggestion, int position = 1, int total = 1)
    {
        editorSuggestion = suggestion;
        EditorSuggestionNote = string.Empty;
        editorPosition = position;
        editorTotal = Math.Max(total, position);
        NotifyEditorSuggestionChanged();
    }

    /// <summary>Há uma sugestão do editor e ela ainda vale: a linha gravada tem a tradução em que ela foi feita e não está aprovada.</summary>
    public bool HasEditorSuggestion => editorSuggestion is { } s && s.IsLiveFor(Saved);

    /// <summary>O título do cartão no inspetor.</summary>
    public string EditorSuggestionTitle => "Passada de editor";

    /// <summary>O título do cartão no Modo revisão, com o tipo ("Passada de editor · voz").</summary>
    public string EditorSuggestionReviewTitle => editorSuggestion is { } s ? $"Passada de editor · {s.KindLabel}" : string.Empty;

    /// <summary>A etiqueta do cartão no inspetor ("fluidez · 1 de 14").</summary>
    public string EditorSuggestionChip => editorSuggestion is { } s ? $"{s.KindLabel} · {editorPosition} de {editorTotal}" : string.Empty;

    /// <summary>A etiqueta do cartão no Modo revisão ("2 de 14 na cena").</summary>
    public string EditorSuggestionReviewChip => editorSuggestion is null ? string.Empty : $"{editorPosition} de {editorTotal} na cena";

    /// <summary>A tradução de hoje, sem marcadores de pausa, para o texto riscado.</summary>
    public string EditorSuggestionCurrent => LineText.Plain(editorSuggestion?.Base);

    /// <summary>O texto proposto, sem marcadores de pausa.</summary>
    public string EditorSuggestionProposed => LineText.Plain(editorSuggestion?.Proposed);

    /// <summary>O motivo, em uma frase.</summary>
    public string EditorSuggestionReason => editorSuggestion?.Reason ?? string.Empty;

    /// <summary>Troca a tradução pela proposta e grava na hora: aceitar é uma decisão da pessoa, sem espera. Se a gravação falhar, a sugestão continua e o erro aparece como em qualquer edição.</summary>
    [RelayCommand(CanExecute = nameof(HasEditorSuggestion))]
    private void AcceptEditorSuggestion()
    {
        if (editorSuggestion is not { } suggestion)
        {
            return;
        }

        Translation = suggestion.Proposed;
        if (Flush())
        {
            Resolve(suggestion);
        }
    }

    /// <summary>Tira a sugestão sem mexer na tradução.</summary>
    [RelayCommand(CanExecute = nameof(HasEditorSuggestion))]
    private void IgnoreEditorSuggestion()
    {
        if (editorSuggestion is { } suggestion)
        {
            Resolve(suggestion);
        }
    }

    private bool CanAskAnotherEditorSuggestion() => HasEditorSuggestion && !IsAskingAnotherEditorSuggestion && EditorSuggestionAnother is not null;

    /// <summary>"Outra": pede ao editor uma proposta diferente para a linha. A tradução não muda; se o pedido falha, a proposta de antes continua no cartão.</summary>
    [RelayCommand(CanExecute = nameof(CanAskAnotherEditorSuggestion))]
    private async Task OtherEditorSuggestionAsync()
    {
        if (editorSuggestion is not { } current || EditorSuggestionAnother is not { } ask || disposed)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        anotherCancellation = cancellation;
        EditorSuggestionNote = string.Empty;
        IsAskingAnotherEditorSuggestion = true;
        try
        {
            var another = await ask(current, cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(editorSuggestion, current))
            {
                return;
            }

            if (another is null)
            {
                EditorSuggestionNote = "O editor não achou outra forma melhor. A sugestão de antes continua.";
                return;
            }

            editorSuggestion = another;
            NotifyEditorSuggestionChanged();
            EditorSuggestionReplaced?.Invoke(another);
        }
        catch (Exception e)
        {
            // Um pedido cancelado por Dispose não mostra erro; o resto (rede, SDK, recusa) vira uma frase.
            if (!cancellation.IsCancellationRequested)
            {
                EditorSuggestionNote = $"Não foi possível pedir outra sugestão: {ClaudeErrorMessages.Describe(e)} A tradução não mudou.";
            }
        }
        finally
        {
            if (ReferenceEquals(anotherCancellation, cancellation))
            {
                anotherCancellation = null;
            }

            IsAskingAnotherEditorSuggestion = false;
        }
    }

    private void CancelAnotherEditorSuggestion() => anotherCancellation?.Cancel();

    private void Resolve(EditorSuggestion suggestion)
    {
        editorSuggestion = null;
        NotifyEditorSuggestionChanged();
        EditorSuggestionResolved?.Invoke(suggestion);
    }

    private void NotifyEditorSuggestionChanged()
    {
        OnPropertyChanged(nameof(HasEditorSuggestion));
        OnPropertyChanged(nameof(IsCompactInspector));
        OnPropertyChanged(nameof(IsSceneCollapsed));
        OnPropertyChanged(nameof(TranslationMinHeight));
        OnPropertyChanged(nameof(EditorSuggestionReviewTitle));
        OnPropertyChanged(nameof(EditorSuggestionChip));
        OnPropertyChanged(nameof(EditorSuggestionReviewChip));
        OnPropertyChanged(nameof(EditorSuggestionCurrent));
        OnPropertyChanged(nameof(EditorSuggestionProposed));
        OnPropertyChanged(nameof(EditorSuggestionReason));
        AcceptEditorSuggestionCommand.NotifyCanExecuteChanged();
        IgnoreEditorSuggestionCommand.NotifyCanExecuteChanged();
        OtherEditorSuggestionCommand.NotifyCanExecuteChanged();
    }
}
