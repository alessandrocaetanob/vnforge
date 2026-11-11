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
    public async Task Autorizar_acima_do_aviso_sem_quem_responda_recusa_para_nao_gastar_sem_perguntar()
    {
        var guard = Guard(100m, 5m, 0m);

        Assert.Equal(BudgetStop.Declined, await guard.AuthorizeAsync(50m, CancellationToken.None));
    }

    [Fact]
    public void Recusa_limite_ou_aviso_negativo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetLimits(-1m, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetLimits(null, -1m));
    }
}
