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
