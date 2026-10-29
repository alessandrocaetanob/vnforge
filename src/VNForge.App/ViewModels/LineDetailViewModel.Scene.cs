using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

public sealed partial class LineDetailViewModel
{
    /// <summary>"Na cena": a fala anterior e a seguinte do script, sem marcadores, na ordem em que aparecem; vazio quando o Editor não as carregou.</summary>
    public IReadOnlyList<ContextLine> Scene { get; init; } = [];

    public bool HasScene => Scene.Count > 0;

    /// <summary>As outras linhas do projeto com o mesmo original; nulo quando o Editor não montou o índice.</summary>
    public ISameSourceLines? SameSource
    {
        get => sameSource;
        init
        {
            sameSource = value;
            if (value is not null)
            {
                value.Batch.PropertyChanged += OnBatchChanged;
            }
        }
    }

    private readonly ISameSourceLines? sameSource;

    public int SameSourceCount => SameSource?.Count ?? 0;

    public bool HasSameSource => SameSourceCount > 0;

    public string SameSourceText => SameSourceCount == 1 ? "Esta fala aparece em mais 1 linha." : $"Esta fala aparece em mais {ProjectLabels.Number(SameSourceCount)} linhas.";

    /// <summary>O que o "Usar em todos" fez, em uma frase; nulo antes de usar.</summary>
    [ObservableProperty]
    public partial string? SameSourceResult { get; private set; }

    /// <summary>Um "Usar em todos" está gravando fora da thread da tela (esta linha ou outra do Editor); os botões ficam desligados e o andamento aparece com o "Cancelar".</summary>
    public bool IsApplyingToAll => SameSource?.Batch.IsRunning ?? false;

    /// <summary>O andamento do "Usar em todos", de 0 a 1.</summary>
    public double ApplyProgress => SameSource?.Batch.Progress ?? 0;

    /// <summary>O que o "Usar em todos" está fazendo, com a porcentagem.</summary>
    public string ApplyProgressText => SameSource?.Batch.Text ?? string.Empty;

    private void OnBatchChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsApplyingToAll));
        OnPropertyChanged(nameof(ApplyProgress));
        OnPropertyChanged(nameof(ApplyProgressText));
        if (e.PropertyName == nameof(SameSourceBatch.LastOutcome) && !startedBatch && SameSource?.Batch.LastOutcome is { } outcome)
        {
            ShowApplyOutcome(outcome);
        }

        if (e.PropertyName == nameof(SameSourceBatch.IsRunning))
        {
            UseForAllCommand.NotifyCanExecuteChanged();
            ConfirmUseForAllCommand.NotifyCanExecuteChanged();
            UseVariantCommand.NotifyCanExecuteChanged();
            CancelApplyToAllCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Este painel começou o lote que está rodando e mostra o resultado dele; os outros painéis o mostram pelo <see cref="SameSourceBatch.LastOutcome"/>.</summary>
    private bool startedBatch;

    private void StopObservingBatch()
    {
        if (SameSource is { } source)
        {
            source.Batch.PropertyChanged -= OnBatchChanged;
        }
    }

    private bool CanStartApply() => !IsApplyingToAll;

    /// <summary>O diálogo "Substituir todas?" está aberto: alguma das outras linhas já tem outra tradução.</summary>
    [ObservableProperty]
    public partial bool IsUseForAllConfirmOpen { get; set; }

    /// <summary>A pergunta do diálogo, com quantas linhas seriam substituídas e quantas delas já estão aprovadas ou revisadas.</summary>
    [ObservableProperty]
    public partial string UseForAllConfirmText { get; private set; } = string.Empty;

    /// <summary>Grava a tradução do campo em todas as outras linhas com o mesmo original. A linha aberta é gravada antes; se outras linhas já têm outra tradução, pergunta primeiro.</summary>
    [RelayCommand(CanExecute = nameof(CanUseForAll))]
    private async Task UseForAllAsync()
    {
        if (IsApplyingToAll)
        {
            return;
        }

        SameSourceResult = null;
        if (string.IsNullOrWhiteSpace(Translation))
        {
            ErrorMessage = "Escreva a tradução desta linha antes de usá-la nas outras.";
            return;
        }

        var preview = new SameSourcePreview(0, 0);
        if (!Flush() || !TrySave(() => preview = SameSource!.Inspect(Translation)))
        {
            return;
        }

        if (preview.Differing == 0)
        {
            await ApplyToAllAsync();
            return;
        }

        UseForAllConfirmText = ConfirmText(preview);
        IsUseForAllConfirmOpen = true;
    }

    private bool CanUseForAll() => HasSameSource && !IsApplyingToAll;

    [RelayCommand(CanExecute = nameof(CanStartApply))]
    private async Task ConfirmUseForAllAsync()
    {
        IsUseForAllConfirmOpen = false;
        if (IsApplyingToAll)
        {
            pendingVariant = null;
            return;
        }

        AdoptPendingVariant();
        await ApplyToAllAsync();
    }

    /// <summary>Para o "Usar em todos" entre um script e outro. O que já foi gravado fica gravado e pode ser desfeito pela lista de tarefas.</summary>
    [RelayCommand(CanExecute = nameof(IsApplyingToAll))]
    private void CancelApplyToAll() => SameSource?.Batch.Cancel();

    [RelayCommand]
    private void CancelUseForAll()
    {
        IsUseForAllConfirmOpen = false;
        pendingVariant = null;
    }

    /// <summary>A tradução de uma variante do cartão (#457) à espera da confirmação: só entra no campo desta linha quando o usuário confirma, para cancelar não mudar nada.</summary>
    private string? pendingVariant;

    private void AdoptPendingVariant()
    {
        if (pendingVariant is { } text)
        {
            pendingVariant = null;
            Translation = text;
        }
    }

    private async Task ApplyToAllAsync()
    {
        if (IsApplyingToAll || !Flush())
        {
            return;
        }

        var outcome = new SameSourceOutcome(0, 0, 0, Failed: false);
        startedBatch = true;
        try
        {
            if (await TrySaveAsync(async () => outcome = await SameSource!.ApplyAsync(Translation)))
            {
                ShowApplyOutcome(outcome);
            }
        }
        finally
        {
            startedBatch = false;
        }
    }

    private void ShowApplyOutcome(SameSourceOutcome outcome)
    {
        var applied = $"Aplicada a {ProjectLabels.Number(outcome.Applied)} de {ProjectLabels.Number(outcome.Requested)} linhas";
        if (outcome.Failed)
        {
            ErrorMessage = outcome.Stale
                ? $"{applied}; as outras não foram gravadas. {outcome.FailureReason} O que já foi gravado pode ser desfeito pela lista de tarefas.{EditedNote(outcome)}"
                : $"{applied}; as outras não foram gravadas. Tente de novo.{EditedNote(outcome)}";
            return;
        }

        SameSourceResult = outcome.Canceled
            ? $"Cancelado. {applied}; as outras continuam como estavam.{EditedNote(outcome)}"
            : ChangedText(outcome.Changed) + EditedNote(outcome);
    }

    private static string ChangedText(int changed) => changed switch
    {
        0 => "As outras linhas já tinham esta tradução.",
        1 => "Aplicada a 1 linha.",
        _ => $"Aplicada a {ProjectLabels.Number(changed)} linhas.",
    };

    private static string EditedNote(SameSourceOutcome outcome) => outcome.Edited switch
    {
        0 => string.Empty,
        1 => " 1 linha foi editada enquanto isso e ficou como estava.",
        _ => $" {ProjectLabels.Number(outcome.Edited)} linhas foram editadas enquanto isso e ficaram como estavam.",
    };

    private static string ConfirmText(SameSourcePreview preview)
    {
        var lines = preview.Differing == 1 ? "1 destas linhas já tem" : $"{ProjectLabels.Number(preview.Differing)} destas linhas já têm";
        var strong = preview.Strong switch
        {
            0 => string.Empty,
            1 => " (1 aprovada)",
            _ => $" ({ProjectLabels.Number(preview.Strong)} aprovadas)",
        };
        return $"{lines} outra tradução{strong}. Substituir todas?";
    }
}
