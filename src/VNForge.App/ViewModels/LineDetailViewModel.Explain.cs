using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Claude;
using VNForge.App.Controls;
using VNForge.App.Services;

namespace VNForge.App.ViewModels;

/// <summary>"Entender o japonês": a explicação da IA do original, só para ler. Fechada por padrão; só o clique no botão pede e gasta.</summary>
public sealed partial class LineDetailViewModel
{
    private CancellationTokenSource? explainCancellation;

    /// <summary>Quem explica o original; sem ele o botão fica desligado.</summary>
    public ILineExplanations? Explanations { get; init; }

    /// <summary>A pasta do projeto, onde a explicação fica guardada (<c>.cache/explain.json</c>); o Editor preenche.</summary>
    public string? ProjectRoot { get; init; }

    public bool CanExplain => Explanations is not null && ProjectRoot is not null;

    /// <summary>Quanto custa pedir a explicação, em reais ("menos de R$ 0,01"); vazio sem serviço.</summary>
    public string ExplainPriceText => Explanations?.PriceText ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExplainCard))]
    public partial bool IsExplainOpen { get; private set; }

    [ObservableProperty]
    public partial bool IsExplaining { get; private set; }

    /// <summary>As partes do original com o significado de cada uma; vazio antes do primeiro pedido.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExplainCard))]
    public partial IReadOnlyList<ExplainPart> ExplainParts { get; private set; } = [];

    /// <summary>Quem explicou, para o cabeçalho do cartão ("Claude Haiku 5.5").</summary>
    [ObservableProperty]
    public partial string ExplainModel { get; private set; } = string.Empty;

    /// <summary>Por que não foi possível explicar, em português; nulo quando deu certo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExplainError))]
    public partial string? ExplainError { get; private set; }

    /// <summary>O cartão aparece aberto e com a explicação pronta.</summary>
    public bool ShowExplainCard => IsExplainOpen && ExplainParts.Count > 0;

    public bool HasExplainError => !string.IsNullOrEmpty(ExplainError);

    /// <summary>Abre o cartão (pedindo a explicação na primeira vez) ou o fecha. Uma explicação que falhou é pedida de novo na próxima abertura.</summary>
    [RelayCommand(CanExecute = nameof(CanExplain))]
    private async Task ToggleExplainAsync()
    {
        if (IsExplainOpen)
        {
            IsExplainOpen = false;
            return;
        }

        IsExplainOpen = true;
        if (ExplainParts.Count == 0 && Explanations is { } explanations && ProjectRoot is { } root && !disposed)
        {
            await LoadExplanationAsync(explanations, root);
        }
    }

    private async Task LoadExplanationAsync(ILineExplanations explanations, string root)
    {
        using var cancellation = new CancellationTokenSource();
        explainCancellation = cancellation;
        ExplainError = null;
        IsExplaining = true;
        try
        {
            var result = await explanations.ExplainAsync(root, unit.Source, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            ExplainParts = [.. result.Parts.Select(p => new ExplainPart(p.Source, p.Meaning))];
            ExplainModel = ProjectLabels.Model(result.Model);
            ExplainError = ExplainParts.Count == 0 ? "Esta fala não tem texto em japonês para explicar." : null;
        }
        catch (Exception e)
        {
            if (!cancellation.IsCancellationRequested)
            {
                ExplainError = e is LineExplanationException ? e.Message : ClaudeErrorMessages.Describe(e);
            }
        }
        finally
        {
            if (ReferenceEquals(explainCancellation, cancellation))
            {
                explainCancellation = null;
            }

            IsExplaining = false;
        }
    }

    private void CancelExplanation() => explainCancellation?.Cancel();
}
