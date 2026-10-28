using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Claude;
using VNForge.App.Services;

namespace VNForge.App.ViewModels;

/// <summary>Sugestão da IA para a linha (Ctrl+Espaço): aparece ao lado da tradução e só vira tradução quando o usuário a aceita.</summary>
public sealed partial class LineDetailViewModel
{
    private CancellationTokenSource? suggestionCancellation;
    private LineSuggestion? suggestionResult;
    private DateTimeOffset suggestedAt;

    /// <summary>Quem pede a sugestão; sem ele o comando fica desligado.</summary>
    public ILineSuggester? Suggester { get; init; }

    /// <summary>O texto sugerido; vazio quando não há sugestão.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestion), nameof(ShowSuggestionCard))]
    public partial string Suggestion { get; private set; } = string.Empty;

    /// <summary>De onde veio a sugestão, para o usuário saber se é do Claude ou do provedor falso.</summary>
    [ObservableProperty]
    public partial string SuggestionSource { get; private set; } = string.Empty;

    /// <summary>Falha ao pedir a sugestão, em português; nulo quando o último pedido deu certo.</summary>
    [ObservableProperty]
    public partial string? SuggestionError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestButtonText), nameof(ShowSuggestionCard))]
    [NotifyCanExecuteChangedFor(nameof(CancelSuggestionCommand))]
    public partial bool IsSuggesting { get; private set; }

    public string SuggestButtonText => IsSuggesting ? "Pedindo sugestão..." : "Sugerir (Ctrl+Espaço)";

    public bool HasSuggestion => Suggestion.Length > 0;

    /// <summary>O cartão da IA aparece com uma sugestão pronta ou enquanto o pedido roda.</summary>
    public bool ShowSuggestionCard => HasSuggestion || IsSuggesting;

    /// <summary>O modelo que respondeu, no cabeçalho do cartão da IA ("Claude Opus 5.5"); vazio sem sugestão.</summary>
    public string SuggestionModel => suggestionResult switch
    {
        null => string.Empty,
        { IsDemo: true } => "IA de demonstração",
        { FromFakeProvider: true } => "Provedor de teste",
        { Model: { Length: > 0 } model } => ProjectLabels.Model(model),
        _ => "IA",
    };

    /// <summary>O resto do cabeçalho do cartão: o esforço e há quanto tempo a sugestão veio ("· esforço baixo · há 2 min").</summary>
    public string SuggestionDetails
    {
        get
        {
            if (suggestionResult is null)
            {
                return string.Empty;
            }

            var how = suggestionResult.IsDemo ? "gratuito"
                : suggestionResult.FromFakeProvider ? "sem chave de API"
                : suggestionResult.Effort is { Length: > 0 } effort ? $"esforço {effort}"
                : string.Empty;
            var age = RelativeTime.Format(suggestedAt, clock.GetUtcNow());
            return how.Length == 0 ? $"· {age}" : $"· {how} · {age}";
        }
    }

    /// <summary>Refaz o "há quanto tempo" do cartão; a tela chama de tempos em tempos enquanto ele está aberto.</summary>
    public void RefreshSuggestionAge() => OnPropertyChanged(nameof(SuggestionDetails));

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
            if (!cancellation.IsCancellationRequested)
            {
                suggestionResult = result;
                suggestedAt = clock.GetUtcNow();
                Suggestion = result.Text;
                SuggestionSource = result.IsDemo
                    ? "Sugestão de demonstração: a cena de exemplo usa uma IA de mentira, sem custo e sem rede."
                    : result.FromFakeProvider
                        ? "Sugestão de teste, sem chave de API. Configure a chave em Ajustes para usar o Claude."
                        : "Sugestão da IA";
            }
        }
        catch (Exception e)
        {
            // O provedor pode lançar de tudo (rede, SDK, recusa); um pedido cancelado por Dispose não mostra erro.
            // Cancelamento sem Dispose é tempo esgotado e entra no mapeamento.
            if (!cancellation.IsCancellationRequested)
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

    [RelayCommand(CanExecute = nameof(IsSuggesting))]
    private void CancelSuggestion() => suggestionCancellation?.Cancel();

    partial void OnSuggestionChanged(string value)
    {
        if (value.Length == 0)
        {
            suggestionResult = null;
        }

        OnPropertyChanged(nameof(SuggestionModel));
        OnPropertyChanged(nameof(SuggestionDetails));
        AcceptSuggestionCommand.NotifyCanExecuteChanged();
        DiscardSuggestionCommand.NotifyCanExecuteChanged();
    }
}
