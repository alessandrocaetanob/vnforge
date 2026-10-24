using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Claude;
using VNForge.App.Services;

namespace VNForge.App.ViewModels;

/// <summary>Sugestão da IA para a linha (Ctrl+Espaço): aparece ao lado da tradução e só vira tradução quando o usuário a aceita.</summary>
public sealed partial class LineDetailViewModel
{
    private CancellationTokenSource? suggestionCancellation;

    /// <summary>Quem pede a sugestão; sem ele o comando fica desligado.</summary>
    public ILineSuggester? Suggester { get; init; }

    /// <summary>O texto sugerido; vazio quando não há sugestão.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestion))]
    public partial string Suggestion { get; private set; } = string.Empty;

    /// <summary>De onde veio a sugestão, para o usuário saber se é do Claude ou do provedor falso.</summary>
    [ObservableProperty]
    public partial string SuggestionSource { get; private set; } = string.Empty;

    /// <summary>Falha ao pedir a sugestão, em português; nulo quando o último pedido deu certo.</summary>
    [ObservableProperty]
    public partial string? SuggestionError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestButtonText))]
    public partial bool IsSuggesting { get; private set; }

    public string SuggestButtonText => IsSuggesting ? "Pedindo sugestão..." : "Sugerir (Ctrl+Espaço)";

    public bool HasSuggestion => Suggestion.Length > 0;

    [RelayCommand(CanExecute = nameof(CanSuggest))]
    private async Task SuggestAsync()
    {
        if (Suggester is not { } suggester || disposed)
        {
            return;
        }

        // O RelayCommand assíncrono não deixa dois pedidos correrem juntos, então aqui não há pedido anterior a cancelar.
        using var cancellation = new CancellationTokenSource();
        suggestionCancellation = cancellation;
        Suggestion = string.Empty;
        SuggestionError = null;
        IsSuggesting = true;
        try
        {
            var result = await suggester.SuggestAsync(unit, cancellation.Token);
            if (!cancellation.IsCancellationRequested && !disposed)
            {
                Suggestion = result.Text;
                SuggestionSource = result.FromFakeProvider
                    ? "Sugestão de teste, sem chave de API. Configure a chave em Ajustes para usar o Claude."
                    : "Sugestão da IA";
            }
        }
        catch (Exception e)
        {
            // O provedor pode lançar de tudo (rede, SDK, recusa); um pedido cancelado por Dispose não mostra erro.
            // Cancelamento sem Dispose é tempo esgotado e entra no mapeamento.
            if (!cancellation.IsCancellationRequested && !disposed)
            {
                var reason = e is LineSuggestionException ? e.Message : ClaudeErrorMessages.Describe(e);
                SuggestionError = $"Não foi possível pedir a sugestão: {reason} A tradução da linha não mudou.";
            }
        }
        finally
        {
            if (ReferenceEquals(suggestionCancellation, cancellation))
            {
                suggestionCancellation = null;
            }

            IsSuggesting = false;
        }
    }

    private bool CanSuggest() => Suggester is not null;

    [RelayCommand(CanExecute = nameof(HasSuggestion))]
    private void AcceptSuggestion()
    {
        Translation = Suggestion;
        Suggestion = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(HasSuggestion))]
    private void DiscardSuggestion() => Suggestion = string.Empty;

    private void CancelSuggestion() => suggestionCancellation?.Cancel();

    partial void OnSuggestionChanged(string value)
    {
        AcceptSuggestionCommand.NotifyCanExecuteChanged();
        DiscardSuggestionCommand.NotifyCanExecuteChanged();
    }
}
