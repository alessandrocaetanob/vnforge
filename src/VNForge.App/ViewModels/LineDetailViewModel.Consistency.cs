using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Qa;

namespace VNForge.App.ViewModels;

public sealed partial class LineDetailViewModel
{
    /// <summary>As outras traduções do original desta linha, para o cartão do aviso; nulo quando o Editor não montou o índice.</summary>
    public IConsistencyLines? Consistency { get; init; }

    /// <summary>
    /// O cartão de "mesmo original, traduções diferentes" desta linha: as variantes, as linhas de cada uma e a diferença para a mais usada, como na
    /// Conferência. Só aparece quando o QA aponta esta linha (<see cref="ShowIssues"/>); o aviso solto dá lugar a ele.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConsistencyCard))]
    public partial ConferenceConsistencyCardViewModel? ConsistencyCard { get; private set; }

    public bool HasConsistencyCard => ConsistencyCard is not null;

    /// <summary>Monta o cartão quando a linha tem o aviso da regra e tira o aviso solto da lista; sem cartão o aviso fica como era.</summary>
    private IEnumerable<QaIssue> ShowConsistency(IReadOnlyList<QaIssue> issues, IEnumerable<QaIssue> shown)
    {
        var flagged = issues.Any(i => i.RuleId == InconsistentTranslationRule.RuleId);
        ConsistencyCard = flagged
            ? Consistency?.Build(new ConsistencyCardActions(UseVariantCommand, KeepDifferentCommand, OpenConsistencyLineCommand))
            : null;
        return ConsistencyCard is null ? shown : shown.Where(i => i.RuleId != InconsistentTranslationRule.RuleId);
    }

    /// <summary>
    /// "Usar em todas" de uma variante: grava a tradução dela nesta linha e nas outras do original, pelo "Usar em todos" (a pergunta antes de substituir
    /// traduções diferentes, o "Desfazer" do lote). Cancelar a pergunta não muda nenhuma linha, nem esta.
    /// </summary>
    [RelayCommand]
    private void UseVariant(ConferenceVariantViewModel? variant)
    {
        if (variant is null || SameSource is null || !Flush())
        {
            return;
        }

        SameSourceResult = null;
        var preview = new SameSourcePreview(0, 0);
        if (!TrySave(() => preview = SameSource.Inspect(variant.Translation)))
        {
            return;
        }

        pendingVariant = variant.Translation;
        if (preview.Differing == 0)
        {
            AdoptPendingVariant();
            UseForAll();
            return;
        }

        UseForAllConfirmText = ConfirmText(preview);
        IsUseForAllConfirmOpen = true;
    }

    /// <summary>"Manter diferentes de propósito": o aviso some desta linha e das outras do original até o original ou uma tradução mudar.</summary>
    [RelayCommand]
    private async Task KeepDifferentAsync(ConferenceConsistencyCardViewModel? card)
    {
        if (card is null || Consistency is null || !Flush())
        {
            return;
        }

        SameSourceResult = null;
        if (await Consistency.KeepAsync(card.Group) is { } failure)
        {
            ErrorMessage = failure;
            return;
        }

        ConsistencyCard = null;
    }

    [RelayCommand]
    private Task OpenConsistencyLineAsync(LineRequest? request) => request is null || Consistency is null ? Task.CompletedTask : Consistency.OpenAsync(request);
}
