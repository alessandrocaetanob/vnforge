using VNForge.AI.Cost;

namespace VNForge.AI.Tests.Cost;

public class BudgetGuardTests
{
    private static BudgetGuard Guard(decimal? limit, decimal? warn, decimal spent, Func<BudgetQuestion, CancellationToken, Task<bool>>? confirm = null) =>
        new(new BudgetLimits(limit, warn), () => spent, _ => 0m, confirm);

    [Fact]
    public void Para_quando_o_gasto_chega_ao_limite()
    {
        Assert.Equal(BudgetVerdict.Proceed, Guard(10m, null, 9.99m).Evaluate(0m));
        Assert.Equal(BudgetVerdict.Stop, Guard(10m, null, 10m).Evaluate(0m));
        Assert.Equal(BudgetVerdict.Stop, Guard(10m, null, 12m).Evaluate(0m));
        Assert.True(Guard(10m, null, 10m).LimitReached);
    }

    [Fact]
    public void Pergunta_quando_o_pedido_passa_do_valor_de_aviso()
    {
        Assert.Equal(BudgetVerdict.Ask, Guard(100m, 5m, 0m).Evaluate(5.01m));
    }

    [Fact]
    public void Nao_pergunta_quando_o_pedido_esta_no_valor_de_aviso_ou_abaixo()
    {
        Assert.Equal(BudgetVerdict.Proceed, Guard(100m, 5m, 0m).Evaluate(5m));
        Assert.Equal(BudgetVerdict.Proceed, Guard(100m, 5m, 0m).Evaluate(0.10m));
    }

    [Fact]
    public void Sem_limite_nem_aviso_deixa_passar_qualquer_pedido()
    {
        var guard = Guard(null, null, 9999m);

        Assert.Equal(BudgetVerdict.Proceed, guard.Evaluate(500m));
        Assert.False(guard.LimitReached);
    }

    [Fact]
    public void O_limite_vence_o_aviso()
    {
        Assert.Equal(BudgetVerdict.Stop, Guard(10m, 1m, 10m).Evaluate(50m));
    }

    [Fact]
    public async Task Autorizar_pergunta_so_acima_do_aviso_e_respeita_a_resposta()
    {
        var asked = new List<BudgetQuestion>();
        var guard = Guard(100m, 5m, 20m, (q, _) =>
        {
            asked.Add(q);
            return Task.FromResult(false);
        });

        Assert.Null(await guard.AuthorizeAsync(4m, CancellationToken.None));
        Assert.Empty(asked);

        Assert.Equal(BudgetStop.Declined, await guard.AuthorizeAsync(6m, CancellationToken.None));
        var question = Assert.Single(asked);
        Assert.Equal(6m, question.EstimatedCost);
        Assert.Equal(20m, question.Spent);
        Assert.Equal(100m, question.Limit);
    }

    [Fact]
    public async Task Autorizar_com_resposta_sim_deixa_o_pedido_seguir()
    {
        var guard = Guard(100m, 5m, 0m, (_, _) => Task.FromResult(true));

        Assert.Null(await guard.AuthorizeAsync(50m, CancellationToken.None));
    }

    [Fact]
    public async Task Autorizar_acima_do_aviso_sem_quem_responda_recusa_para_nao_gastar_sem_perguntar()
    {
        var guard = Guard(100m, 5m, 0m);

        Assert.Equal(BudgetStop.Declined, await guard.AuthorizeAsync(50m, CancellationToken.None));
    }

    [Fact]
    public async Task Autorizar_no_limite_nao_pergunta_e_para()
    {
        var asks = 0;
        var guard = Guard(10m, 5m, 10m, (_, _) =>
        {
            asks++;
            return Task.FromResult(true);
        });

        Assert.Equal(BudgetStop.LimitReached, await guard.AuthorizeAsync(50m, CancellationToken.None));
        Assert.Equal(0, asks);
    }

    [Fact]
    public void Recusa_limite_ou_aviso_negativo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetLimits(-1m, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetLimits(null, -1m));
    }

    [Fact]
    public void Reserva_conta_o_que_os_lotes_em_andamento_ainda_vao_gastar()
    {
        var guard = Guard(5m, null, 1m);

        Assert.True(guard.TryReserve(2m));
        Assert.True(guard.TryReserve(2m));
        Assert.False(guard.TryReserve(2m));

        guard.Release(2m);

        Assert.True(guard.TryReserve(2m));
    }

    [Fact]
    public void Sem_limite_a_reserva_sempre_cabe()
    {
        var guard = Guard(null, null, 9999m);

        Assert.True(guard.TryReserve(1000m));
    }

    [Fact]
    public void No_limite_nem_um_lote_de_custo_zero_comeca()
    {
        Assert.False(Guard(5m, null, 5m).TryReserve(0m));
    }
}
