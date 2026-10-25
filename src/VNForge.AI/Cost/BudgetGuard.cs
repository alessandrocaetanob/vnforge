using VNForge.Core.Text;

namespace VNForge.AI.Cost;

/// <summary>Limite de gasto e valor de aviso, em reais. <see langword="null"/> desliga cada um.</summary>
/// <param name="Limit">O gasto acumulado em que o app para.</param>
/// <param name="WarnAbove">O app pergunta antes de um pedido que custa mais que isto.</param>
public sealed record BudgetLimits(decimal? Limit, decimal? WarnAbove)
{
    public decimal? Limit { get; } = Limit is < 0m ? throw new ArgumentOutOfRangeException(nameof(Limit), "O limite não pode ser negativo.") : Limit;

    public decimal? WarnAbove { get; } = WarnAbove is < 0m ? throw new ArgumentOutOfRangeException(nameof(WarnAbove), "O valor de aviso não pode ser negativo.") : WarnAbove;

    public static BudgetLimits None { get; } = new(null, null);
}

/// <summary>O que o app mostra quando pergunta se o pedido pode seguir; valores em reais.</summary>
public sealed record BudgetQuestion(decimal EstimatedCost, decimal Spent, decimal? Limit, decimal WarnAbove);

public enum BudgetVerdict
{
    Proceed,
    Ask,
    Stop,
}

/// <summary>Por que uma execução parou antes de terminar por causa do orçamento.</summary>
public enum BudgetStop
{
    /// <summary>O gasto acumulado chegou ao limite.</summary>
    LimitReached,

    /// <summary>A pessoa respondeu "não" à pergunta sobre o pedido (ou não havia quem respondesse).</summary>
    Declined,
}

/// <summary>
/// Guarda do gasto de uma execução, em reais. <see cref="Evaluate"/> decide: <see cref="BudgetVerdict.Stop"/> quando o gasto acumulado chega
/// ao limite (vale mais que o aviso), <see cref="BudgetVerdict.Ask"/> quando o pedido custa mais que o valor de aviso e
/// <see cref="BudgetVerdict.Proceed"/> no resto. Como as chamadas rodam em paralelo, a execução pode passar do limite por até um lote por
/// chamada em andamento: a parada vale para os lotes que ainda não começaram.
/// </summary>
/// <param name="spent">Lê o gasto acumulado até agora (o já gravado no projeto mais o desta execução); chamado de várias threads.</param>
/// <param name="estimate">Estima em reais o custo de um conjunto de linhas.</param>
/// <param name="confirm">Pergunta à pessoa; sem ele, um pedido acima do aviso é recusado em vez de gasto sem perguntar.</param>
public sealed class BudgetGuard(
    BudgetLimits limits,
    Func<decimal> spent,
    Func<IReadOnlyList<TextUnit>, decimal> estimate,
    Func<BudgetQuestion, CancellationToken, Task<bool>>? confirm = null)
{
    private readonly BudgetLimits _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    private readonly Func<decimal> _spent = spent ?? throw new ArgumentNullException(nameof(spent));
    private readonly Func<IReadOnlyList<TextUnit>, decimal> _estimate = estimate ?? throw new ArgumentNullException(nameof(estimate));
    private readonly Lock _gate = new();
    private decimal _reserved;

    public bool LimitReached => _limits.Limit is { } limit && _spent() >= limit;

    /// <summary>O custo, em reais, de pedir a tradução de <paramref name="lines"/>.</summary>
    public decimal EstimateRequest(IReadOnlyList<TextUnit> lines) => _estimate(lines);

    /// <summary>
    /// Reserva o custo estimado de um lote que vai começar. Recusa quando o gasto, o já reservado pelos lotes em andamento e este lote passariam do
    /// limite: é o que mantém o limite verdadeiro com chamadas em paralelo. Sem limite, sempre reserva. Quem reserva chama <see cref="Release"/> ao fim do lote.
    /// </summary>
    public bool TryReserve(decimal estimatedCost)
    {
        lock (_gate)
        {
            if (_limits.Limit is { } limit && (LimitReached || _spent() + _reserved + estimatedCost > limit))
            {
                return false;
            }

            _reserved += estimatedCost;
            return true;
        }
    }

    /// <summary>
    /// Se o lote que já falhou pode ser repetido: o gasto (que inclui o que a tentativa que falhou cobrou) mais tudo o que está reservado, a reserva deste lote
    /// incluída como custo da nova tentativa, tem de caber no limite.
    /// </summary>
    public bool AllowsRetry()
    {
        lock (_gate)
        {
            return _limits.Limit is not { } limit || _spent() + _reserved <= limit;
        }
    }

    /// <summary>Devolve a reserva de um lote que terminou; o custo real já entrou no gasto lido por <c>spent</c>.</summary>
    public void Release(decimal estimatedCost)
    {
        lock (_gate)
        {
            _reserved = Math.Max(0m, _reserved - estimatedCost);
        }
    }

    public BudgetVerdict Evaluate(decimal estimatedCost)
    {
        if (LimitReached)
        {
            return BudgetVerdict.Stop;
        }

        return _limits.WarnAbove is { } warn && estimatedCost > warn ? BudgetVerdict.Ask : BudgetVerdict.Proceed;
    }

    /// <summary>Resolve o pedido inteiro antes de começar: <see langword="null"/> deixa seguir; senão diz por que parou.</summary>
    public async Task<BudgetStop?> AuthorizeAsync(decimal estimatedCost, CancellationToken ct)
    {
        switch (Evaluate(estimatedCost))
        {
            case BudgetVerdict.Stop:
                return BudgetStop.LimitReached;
            case BudgetVerdict.Ask:
                var question = new BudgetQuestion(estimatedCost, _spent(), _limits.Limit, _limits.WarnAbove ?? 0m);
                var approved = confirm is not null && await confirm(question, ct).ConfigureAwait(false);
                return approved ? null : BudgetStop.Declined;
            default:
                return null;
        }
    }
}
