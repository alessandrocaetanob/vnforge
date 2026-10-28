using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Cost;
using VNForge.App.Services;
using VNForge.Core.Projects;
using VNForge.Core.Search;

namespace VNForge.App.ViewModels;

/// <summary>
/// O pedido em lote escrito em português (paleta de comandos): três passos no mesmo painel. <see cref="BeginRequestAsync"/> acha as falas e mostra a estimativa
/// de custo sem chamar a IA; <see cref="RunRequestCommand"/> chama a IA e mostra a prévia, com antes e depois por linha, como a da substituição;
/// <see cref="ApplyCommand"/> grava, e só ele, pelo ponto de restauração.
/// </summary>
public sealed partial class SearchReplaceViewModel
{
    private string? requestTitle;
    private ReplacePreview? requestPreview;

    /// <summary>O pedido em lote; nulo desliga o recurso (os testes do painel sem IA).</summary>
    public BatchRequestService? Requests { get; set; }

    /// <summary>O pedido à espera da confirmação do custo; nulo fora desse passo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRequestPlan))]
    [NotifyCanExecuteChangedFor(nameof(RunRequestCommand), nameof(DiscardRequestCommand))]
    public partial BatchRequestPlan? RequestPlan { get; private set; }

    public bool HasRequestPlan => RequestPlan is not null;

    /// <summary>Procura as falas que o pedido alcança e mostra o custo estimado. Não chama a IA nem grava nada.</summary>
    public Task BeginRequestAsync(string request)
    {
        if (IsBusy)
        {
            ErrorMessage = "Espere a operação em andamento terminar, ou cancele, para fazer um novo pedido.";
            return Task.CompletedTask;
        }

        Invalidate();
        if (Project is null || Requests is null)
        {
            ErrorMessage = "Abra um projeto para pedir à IA.";
            return Task.CompletedTask;
        }

        var root = Project.Root;
        var service = Requests;
        return RunAsync(
            async token =>
            {
                var plan = await Task.Run(() => service.Plan(ProjectFolder.Open(root), request, token), token);
                return () =>
                {
                    RequestPlan = plan;
                    Summary = DescribePlan(plan);
                };
            },
            cancellable: true);
    }

    [RelayCommand(CanExecute = nameof(CanRunRequest))]
    private async Task RunRequestAsync()
    {
        var plan = RequestPlan;
        if (plan is null || Project is null || Requests is null)
        {
            return;
        }

        var root = Project.Root;
        var service = Requests;
        await RunAsync(
            async token =>
            {
                var result = await Task.Run(() => service.RunAsync(ProjectFolder.Open(root), plan, token), token);
                return () =>
                {
                    preview = result.Preview;
                    requestPreview = result.Preview;
                    requestTitle = $"Pedido em lote: {Quoted(plan.Request)}";
                    RequestPlan = null;
                    SelectedResult = null;
                    Results = [.. result.Preview.Items.Select(i => new SearchResultRow(Location(i.ScriptKey, i.Number), i.Before, i.After, 0, FirstDifference(i.Before, i.After), i.ScriptKey, i.UnitId))];
                    Summary = DescribeRequestResult(result);
                    SkippedNotes = [];
                    OnPropertyChanged(nameof(HasPreview));
                    ApplyCommand.NotifyCanExecuteChanged();
                };
            },
            cancellable: true);
    }

    [RelayCommand(CanExecute = nameof(HasRequestPlan))]
    private void DiscardRequest()
    {
        Invalidate();
        Summary = "Pedido descartado. Nada foi gasto nem gravado.";
    }

    /// <summary>O título da tarefa que dá o "Desfazer" quando a prévia <paramref name="planned"/> veio de um pedido em lote; nulo para a prévia de uma substituição.</summary>
    private string? RequestTitleFor(ReplacePreview planned) => ReferenceEquals(planned, requestPreview) ? requestTitle : null;

    private bool CanRunRequest() => RequestPlan is not null && !IsBusy;

    partial void OnIsBusyChanged(bool value) => RunRequestCommand.NotifyCanExecuteChanged();

    private static string DescribePlan(BatchRequestPlan plan)
    {
        var scope = plan.Scope;
        var filters = new List<string>();
        if (plan.SpeakerNames.Count > 0)
        {
            filters.Add($"de {string.Join(" e ", plan.SpeakerNames)}");
        }

        if (scope.Scripts.Count > 0)
        {
            filters.Add($"em {string.Join(", ", scope.Scripts)}");
        }

        if (scope.Terms.Count > 0)
        {
            filters.Add($"com {string.Join(" ou ", scope.Terms.Select(t => $"“{t}”"))}");
        }

        var lines = Plural(plan.Candidates.Count, "fala", "falas");
        var what = scope.IsNarrowed
            ? $"A IA vai ler {lines} {string.Join(' ', filters)}."
            : $"O pedido não cita personagem, cena nem palavra: a IA vai ler todas as {lines} traduzidas.";
        return $"{what} Custo estimado: até {plan.CostText}. Nada é gravado: depois você vê a prévia e confirma em Aplicar.";
    }

    private static string DescribeRequestResult(BatchRequestResult result)
    {
        var outcome = result.Outcome;
        var read = outcome.Items.Count + outcome.Unchanged + outcome.Refused;
        var parts = new List<string>();
        if (outcome.Canceled)
        {
            parts.Add("Pedido cancelado: a prévia tem só as linhas que já voltaram.");
        }

        parts.Add(
            outcome.Items.Count == 0
                ? $"A IA leu {Plural(read, "fala", "falas")} e não mudou nenhuma."
                : $"A IA reescreveu {Plural(outcome.Items.Count, "linha", "linhas")} de {Plural(read, "lida", "lidas")}. Nada foi gravado ainda.");

        var preview = result.Preview;
        if (preview.ApprovedLines > 0 || preview.ReviewedLines > 0)
        {
            var lost = new List<string>();
            if (preview.ApprovedLines > 0)
            {
                lost.Add(Plural(preview.ApprovedLines, "aprovada", "aprovadas"));
            }

            if (preview.ReviewedLines > 0)
            {
                lost.Add(Plural(preview.ReviewedLines, "revisada", "revisadas"));
            }

            parts.Add($"Voltam para Traduzida e precisam de nova revisão: {string.Join(" e ", lost)}.");
        }

        if (outcome.Refused > 0)
        {
            parts.Add($"Linhas deixadas como estão porque a IA recusou ou mudou os marcadores de controle: {ProjectLabels.Number(outcome.Refused)}.");
        }

        if (outcome.Failed > 0)
        {
            parts.Add($"{Plural(outcome.Failed, "linha ficou", "linhas ficaram")} como está porque o lote não voltou. {outcome.Problem}".TrimEnd());
        }

        if (outcome.Stopped == BudgetStop.LimitReached)
        {
            parts.Add("O pedido parou no limite de gasto; as outras falas não foram lidas.");
        }

        parts.Add($"Gasto: {Controls.Money.BrlEstimate(result.SpentBrl)}.");
        return string.Join(' ', parts);
    }
}
