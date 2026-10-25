using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

/// <summary>O <see cref="BudgetGuard"/> no runner de lotes, com o provedor falso e sem rede.</summary>
public class TranslationRunnerBudgetTests
{
    private const decimal CostPerLine = 0.10m;

    private static TextUnit Unit(int n) =>
        new($"u{n:D3}", TextUnitKind.Dialogue, null, $"原文{n}", null, TranslationStatus.Untranslated);

    private static List<TextUnit> Units(int count) => [.. Enumerable.Range(0, count).Select(Unit)];

    private static TranslationBatch Build(IReadOnlyList<TextUnit> lines) => new(lines);

    private sealed class Wallet
    {
        private decimal _spent;

        public decimal Spent
        {
            get { lock (this) return _spent; }
        }

        public void Pay(decimal amount)
        {
            lock (this) _spent += amount;
        }
    }

    private static (TranslationRunner Runner, FakeTranslationProvider Provider) Create(
        Wallet wallet, BudgetLimits limits, Func<BudgetQuestion, CancellationToken, Task<bool>>? confirm = null)
    {
        var provider = new FakeTranslationProvider(unit =>
        {
            wallet.Pay(CostPerLine);
            return $"[PT] {unit.Source}";
        });
        var guard = new BudgetGuard(limits, () => wallet.Spent, lines => lines.Count * CostPerLine, confirm);
        var options = new TranslationRunOptions { BatchSize = 20, MaxConcurrency = 1, Delay = (_, _) => Task.CompletedTask, Budget = guard };
        return (new TranslationRunner(provider, options), provider);
    }

    [Fact]
    public async Task Para_quando_o_gasto_acumulado_chega_ao_limite_e_guarda_o_que_ja_voltou()
    {
        var wallet = new Wallet();
        var (runner, provider) = Create(wallet, new BudgetLimits(4m, null));

        var run = await runner.RunAsync(Units(100), Build, ct: CancellationToken.None);

        // Cada lote de 20 linhas custa 2,00: o terceiro lote não começa porque o segundo fechou em 4,00.
        Assert.Equal(2, provider.ReceivedBatches.Count);
        Assert.Equal(40, run.Results.Count);
        Assert.Equal(BudgetStop.LimitReached, run.BudgetStop);
        Assert.Empty(run.Failures);
        Assert.False(run.Canceled);
    }

    [Fact]
    public async Task Pergunta_antes_de_um_pedido_acima_do_valor_de_aviso()
    {
        var wallet = new Wallet();
        var asked = new List<BudgetQuestion>();
        var (runner, provider) = Create(wallet, new BudgetLimits(null, 5m), (q, _) =>
        {
            asked.Add(q);
            return Task.FromResult(true);
        });

        var run = await runner.RunAsync(Units(60), Build, ct: CancellationToken.None);

        var question = Assert.Single(asked);
        Assert.Equal(6.00m, question.EstimatedCost);
        Assert.Equal(60, run.Results.Count);
        Assert.Equal(3, provider.ReceivedBatches.Count);
        Assert.Null(run.BudgetStop);
    }

    [Fact]
    public async Task Resposta_nao_nao_chama_o_provedor()
    {
        var wallet = new Wallet();
        var (runner, provider) = Create(wallet, new BudgetLimits(null, 5m), (_, _) => Task.FromResult(false));

        var run = await runner.RunAsync(Units(60), Build, ct: CancellationToken.None);

        Assert.Empty(provider.ReceivedBatches);
        Assert.Empty(run.Results);
        Assert.Equal(BudgetStop.Declined, run.BudgetStop);
    }

    [Fact]
    public async Task Nao_pergunta_quando_o_pedido_fica_abaixo_do_valor_de_aviso()
    {
        var wallet = new Wallet();
        var asks = 0;
        var (runner, _) = Create(wallet, new BudgetLimits(null, 5m), (_, _) =>
        {
            asks++;
            return Task.FromResult(true);
        });

        var run = await runner.RunAsync(Units(40), Build, ct: CancellationToken.None);

        Assert.Equal(0, asks);
        Assert.Equal(40, run.Results.Count);
    }

    [Fact]
    public async Task Ja_no_limite_antes_de_comecar_nao_chama_o_provedor_nem_pergunta()
    {
        var wallet = new Wallet();
        wallet.Pay(10m);
        var asks = 0;
        var (runner, provider) = Create(wallet, new BudgetLimits(10m, 1m), (_, _) =>
        {
            asks++;
            return Task.FromResult(true);
        });

        var run = await runner.RunAsync(Units(40), Build, ct: CancellationToken.None);

        Assert.Empty(provider.ReceivedBatches);
        Assert.Equal(0, asks);
        Assert.Equal(BudgetStop.LimitReached, run.BudgetStop);
    }

    [Fact]
    public async Task Sem_guarda_o_runner_se_comporta_como_antes()
    {
        var provider = new FakeTranslationProvider();
        var runner = new TranslationRunner(provider, new TranslationRunOptions { BatchSize = 20 });

        var run = await runner.RunAsync(Units(45), Build, ct: CancellationToken.None);

        Assert.Equal(45, run.Results.Count);
        Assert.Null(run.BudgetStop);
    }

    [Fact]
    public async Task Cancelar_enquanto_a_pergunta_espera_devolve_cancelado_sem_lancar()
    {
        var wallet = new Wallet();
        using var cts = new CancellationTokenSource();
        var (runner, provider) = Create(wallet, new BudgetLimits(null, 5m), async (_, token) =>
        {
            await cts.CancelAsync();
            token.ThrowIfCancellationRequested();
            return true;
        });

        var run = await runner.RunAsync(Units(60), Build, ct: cts.Token);

        Assert.True(run.Canceled);
        Assert.Empty(provider.ReceivedBatches);
        Assert.Null(run.BudgetStop);
    }

    /// <summary>Provedor que só responde quando o teste libera, para as chamadas ficarem em andamento juntas.</summary>
    private sealed class HeldProvider(Wallet wallet, int expectedInFlight) : ITranslationProvider
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _inFlight;
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public async Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            if (Interlocked.Increment(ref _inFlight) >= expectedInFlight)
                _started.TrySetResult();

            await _release.Task.ConfigureAwait(false);
            foreach (var _ in batch.Lines)
                wallet.Pay(CostPerLine);
            Interlocked.Decrement(ref _inFlight);
            return [.. batch.Lines.Select(u => new TranslationResult(u.Id, $"[PT] {u.Source}"))];
        }
    }

    [Fact]
    public async Task Com_chamadas_em_paralelo_o_gasto_nao_passa_do_limite()
    {
        var wallet = new Wallet();
        var provider = new HeldProvider(wallet, expectedInFlight: 2);
        var evaluated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var estimates = 0;
        // Um pedido de 4 lotes de 2,00 contra um limite de 5,00: só dois lotes cabem. A primeira estimativa é a do pedido inteiro.
        var guard = new BudgetGuard(new BudgetLimits(5m, null), () => wallet.Spent, lines =>
        {
            if (Interlocked.Increment(ref estimates) == 5)
                evaluated.TrySetResult();
            return lines.Count * CostPerLine;
        });
        var options = new TranslationRunOptions { BatchSize = 20, MaxConcurrency = 4, Delay = (_, _) => Task.CompletedTask, Budget = guard };
        var runner = new TranslationRunner(provider, options);

        var running = runner.RunAsync(Units(80), Build, ct: CancellationToken.None);
        await Task.WhenAll(provider.Started, evaluated.Task);
        provider.Release();
        var run = await running;

        Assert.Equal(2, provider.Calls);
        Assert.Equal(40, run.Results.Count);
        Assert.True(wallet.Spent <= 5m, $"gastou {wallet.Spent}");
        Assert.Equal(BudgetStop.LimitReached, run.BudgetStop);
        Assert.Empty(run.Failures);
    }

    [Fact]
    public async Task Nova_tentativa_nao_comeca_depois_que_o_limite_chegou()
    {
        var wallet = new Wallet();
        var calls = 0;
        var failing = new FailingOnceProvider(wallet, () => calls++);
        var guard = new BudgetGuard(new BudgetLimits(2m, null), () => wallet.Spent, lines => lines.Count * CostPerLine);
        var options = new TranslationRunOptions { BatchSize = 20, MaxConcurrency = 1, Delay = (_, _) => Task.CompletedTask, Budget = guard };
        var runner = new TranslationRunner(failing, options);

        var run = await runner.RunAsync(Units(20), Build, ct: CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Empty(run.Results);
        Assert.Empty(run.Failures);
        Assert.Equal(BudgetStop.LimitReached, run.BudgetStop);
    }

    /// <summary>Cobra o lote e falha: a repetição seria um segundo pagamento.</summary>
    private sealed class FailingOnceProvider(Wallet wallet, Action onCall) : ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
        {
            onCall();
            wallet.Pay(batch.Lines.Count * CostPerLine);
            throw new InvalidOperationException("falha de rede simulada");
        }
    }
}
