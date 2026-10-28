using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Controls;
using VNForge.App.Services;

namespace VNForge.App.ViewModels;

public sealed partial class LineDetailViewModel
{
    /// <summary>"Na cena": a fala anterior e a seguinte do script, sem marcadores, na ordem em que aparecem; vazio quando o Editor não as carregou.</summary>
    public IReadOnlyList<ContextLine> Scene { get; init; } = [];

    public bool HasScene => Scene.Count > 0;

    /// <summary>As outras linhas do projeto com o mesmo original; nulo quando o Editor não montou o índice.</summary>
    public ISameSourceLines? SameSource { get; init; }

    public int SameSourceCount => SameSource?.Count ?? 0;

    public bool HasSameSource => SameSourceCount > 0;

    public string SameSourceText => SameSourceCount == 1 ? "Esta fala aparece em mais 1 linha." : $"Esta fala aparece em mais {ProjectLabels.Number(SameSourceCount)} linhas.";

    /// <summary>O que o "Usar em todos" fez, em uma frase; nulo antes de usar.</summary>
    [ObservableProperty]
    public partial string? SameSourceResult { get; private set; }

    /// <summary>O diálogo "Substituir todas?" está aberto: alguma das outras linhas já tem outra tradução.</summary>
    [ObservableProperty]
    public partial bool IsUseForAllConfirmOpen { get; set; }

    /// <summary>A pergunta do diálogo, com quantas linhas seriam substituídas e quantas delas já estão aprovadas ou revisadas.</summary>
    [ObservableProperty]
    public partial string UseForAllConfirmText { get; private set; } = string.Empty;

    /// <summary>Grava a tradução do campo em todas as outras linhas com o mesmo original. A linha aberta é gravada antes; se outras linhas já têm outra tradução, pergunta primeiro.</summary>
    [RelayCommand(CanExecute = nameof(HasSameSource))]
    private void UseForAll()
    {
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
            ApplyToAll();
            return;
        }

        UseForAllConfirmText = ConfirmText(preview);
        IsUseForAllConfirmOpen = true;
    }

    [RelayCommand]
    private void ConfirmUseForAll()
    {
        IsUseForAllConfirmOpen = false;
        AdoptPendingVariant();
        ApplyToAll();
    }

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

    private void ApplyToAll()
    {
        var outcome = new SameSourceOutcome(0, 0, 0, Failed: false);
        if (!Flush() || !TrySave(() => outcome = SameSource!.Apply(Translation)))
        {
            return;
        }

        if (outcome.Failed)
        {
            ErrorMessage = $"Aplicada a {ProjectLabels.Number(outcome.Applied)} de {ProjectLabels.Number(outcome.Requested)} linhas; as outras não foram gravadas. Tente de novo.";
            return;
        }

        SameSourceResult = outcome.Changed switch
        {
            0 => "As outras linhas já tinham esta tradução.",
            1 => "Aplicada a 1 linha.",
            _ => $"Aplicada a {ProjectLabels.Number(outcome.Changed)} linhas.",
        };
    }

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
