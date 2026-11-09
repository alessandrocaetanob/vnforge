using CommunityToolkit.Mvvm.Input;
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

    /// <summary>Avisa o Editor que a sugestão foi aceita ou ignorada, para ele tirá-la do arquivo e da grade.</summary>
    public Action<EditorSuggestion>? EditorSuggestionResolved { get; set; }

    /// <summary>Mostra a sugestão da passada de editor, que é a <paramref name="position"/>ª das <paramref name="total"/> da cena; nula esconde o cartão.</summary>
    public void ShowEditorSuggestion(EditorSuggestion? suggestion, int position = 1, int total = 1)
    {
        editorSuggestion = suggestion;
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

    private void Resolve(EditorSuggestion suggestion)
    {
        editorSuggestion = null;
        NotifyEditorSuggestionChanged();
        EditorSuggestionResolved?.Invoke(suggestion);
    }

    private void NotifyEditorSuggestionChanged()
    {
        OnPropertyChanged(nameof(HasEditorSuggestion));
        OnPropertyChanged(nameof(EditorSuggestionReviewTitle));
        OnPropertyChanged(nameof(EditorSuggestionChip));
        OnPropertyChanged(nameof(EditorSuggestionReviewChip));
        OnPropertyChanged(nameof(EditorSuggestionCurrent));
        OnPropertyChanged(nameof(EditorSuggestionProposed));
        OnPropertyChanged(nameof(EditorSuggestionReason));
        AcceptEditorSuggestionCommand.NotifyCanExecuteChanged();
        IgnoreEditorSuggestionCommand.NotifyCanExecuteChanged();
    }
}
