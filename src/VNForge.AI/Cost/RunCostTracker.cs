using VNForge.AI.Claude;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Cost;

/// <summary>
/// O gasto de uma execução de IA, igual no app e no <c>vnf</c>: o que o projeto já gastou antes (<c>.cache/ai-usage.json</c>) mais o que o
/// provedor cobrou até agora, em reais, para o <see cref="BudgetGuard"/>; e o registro do gasto no fim, mesmo quando a execução foi
/// interrompida.
/// </summary>
/// <param name="project">O projeto, cujo gasto anterior e cujo registro valem.</param>
/// <param name="model">O modelo, para o preço.</param>
/// <param name="rate">A cotação que a pessoa informou.</param>
/// <param name="usage">Os tokens que o provedor já gastou; <see langword="null"/> enquanto não houve chamada.</param>
public sealed class RunCostTracker(ProjectFolder project, string model, ExchangeRate rate, Func<ClaudeUsage?> usage)
{
    private readonly decimal baselineDollars = AiUsageLog.Load(project).Dollars;

    public ClaudeUsage? Usage => usage();

    /// <summary>O que a execução gastou, em dólar; <see langword="null"/> sem chamada ou com modelo sem preço na tabela.</summary>
    public decimal? RunDollars() => DollarsFor(model, usage());

    public decimal RunBrl => rate.ToBrl(RunDollars() ?? 0m);

    /// <summary>O gasto do projeto mais o da execução, em reais.</summary>
    public decimal SpentBrl() => rate.ToBrl(baselineDollars + (RunDollars() ?? 0m));

    public BudgetGuard CreateGuard(BudgetLimits limits, Func<IReadOnlyList<TextUnit>, decimal> estimateBrl, Func<BudgetQuestion, CancellationToken, Task<bool>>? confirm) =>
        new(limits, SpentBrl, estimateBrl, confirm);

    /// <summary>
    /// Soma o gasto da execução ao total do projeto, que o Painel mostra. Modelo sem preço na tabela não pode derrubar o comando depois de a
    /// tradução gravada: o custo fica desconhecido (conta em <see cref="AiUsageTotals.UnpricedRuns"/>), nunca zero.
    /// </summary>
    public void Record() => Record(project, model, usage());

    /// <inheritdoc cref="Record()"/>
    public static void Record(ProjectFolder project, string model, ClaudeUsage? usage)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (usage is not { } u)
        {
            return;
        }

        var dollars = DollarsFor(model, u);
        AiUsageLog.TryRecord(project, new AiUsageTotals(
            u.InputTokens, u.OutputTokens, u.CacheReadInputTokens, u.CacheCreationInputTokens, dollars ?? 0m, 1, dollars is null ? 1 : 0));
    }

    public static decimal? DollarsFor(string model, ClaudeUsage? usage)
    {
        if (usage is not { } u)
        {
            return null;
        }

        try
        {
            return ModelPricing.For(model).Cost(new TokenUsage(u.InputTokens, u.OutputTokens, u.CacheReadInputTokens, u.CacheCreationInputTokens));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
