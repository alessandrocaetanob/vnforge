using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.App.Services;
using VNForge.Core.Formatting;

namespace VNForge.App.ViewModels;

/// <summary>O aviso "Não cabe na caixa do jogo" do inspetor, com "Dividir em páginas" e "Encurtar com a IA".</summary>
public sealed partial class LineDetailViewModel
{
    /// <summary>Quem encurta a fala a pedido do aviso; sem ele o aviso só oferece dividir em páginas.</summary>
    public ILineShortener? Shortener { get; init; }

    /// <summary>O aviso e as duas saídas; nulo sem perfil de formatação legível. Só aparece (<see cref="FitWarningViewModel.IsVisible"/>) quando a tradução passa da caixa.</summary>
    [ObservableProperty]
    public partial FitWarningViewModel? Fit { get; private set; }

    private void AttachFit(FormattingProfile? profile)
    {
        Fit?.Cancel();
        Fit = profile is null
            ? null
            : new FitWarningViewModel(unit, profile, Shortener, () => Translation, ApplySplit, OfferShortened);
        Fit?.Update();
    }

    private Task ApplySplit(string text)
    {
        Translation = text;
        return Task.CompletedTask;
    }

    /// <summary>A versão curta da IA entra como sugestão, para a pessoa conferir o sentido antes de trocar a tradução.</summary>
    private Task OfferShortened(string text)
    {
        suggestionResult = new LineSuggestion(text, FromFakeProvider: false);
        suggestedAt = clock.GetUtcNow();
        SuggestionError = null;
        Suggestion = text;
        SuggestionSource = "Versão mais curta, feita pela IA. Confira o sentido antes de usar.";
        return Task.CompletedTask;
    }
}
