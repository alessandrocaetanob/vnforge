using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

public class TranslationRunnerTests
{
    private static TextUnit Unit(int n) =>
        new($"u{n:D3}", TextUnitKind.Dialogue, null, $"原文{n}", null, TranslationStatus.Untranslated);

    private static List<TextUnit> Units(int count) => [.. Enumerable.Range(0, count).Select(Unit)];

    private static TranslationBatch Build(IReadOnlyList<TextUnit> lines) => new(lines);

    private static TranslationRunOptions NoWait(int batchSize = 30, int concurrency = 2, int retries = 3) => new()
    {
        BatchSize = batchSize,
        MaxConcurrency = concurrency,
        MaxRetries = retries,
        Delay = (_, _) => Task.CompletedTask,
    };

    /// <summary>Provedor que repassa para uma função, para controlar falha, atraso e cancelamento por chamada.</summary>
    private sealed class ScriptedProvider(Func<TranslationBatch, int, CancellationToken, Task<IReadOnlyList<TranslationResult>>> handler) : ITranslationProvider
    {
        private int _calls;
        public int Calls => _calls;

        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct) =>
            handler(batch, Interlocked.Increment(ref _calls), ct);
    }

    private static IReadOnlyList<TranslationResult> Ok(TranslationBatch b) =>
        [.. b.Lines.Select(u => new TranslationResult(u.Id, $"[PT] {u.Source}"))];

    [Fact]
    public async Task Divide_em_lotes_do_tamanho_configurado_e_devolve_na_ordem_das_linhas()
    {
        var provider = new FakeTranslationProvider();
        var runner = new TranslationRunner(provider, NoWait(batchSize: 20));
        var lines = Units(45);

        var run = await runner.RunAsync(lines, Build, ct: CancellationToken.None);

        Assert.Equal([20, 20, 5], provider.ReceivedBatches.Select(b => b.Lines.Count).Order().Reverse());
        Assert.Equal(lines.Select(l => l.Id), run.Results.Select(r => r.Id));
        Assert.Empty(run.Failures);
        Assert.False(run.Canceled);
    }

    [Theory]
    [InlineData(19)]
    [InlineData(51)]
    public void Recusa_tamanho_de_lote_fora_de_20_a_50(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranslationRunner(new FakeTranslationProvider(), new TranslationRunOptions { BatchSize = size }));
    }

    [Fact]
    public void Padroes_sao_lote_de_30_e_2_chamadas_em_paralelo()
    {
        var options = new TranslationRunOptions();

        Assert.Equal(30, options.BatchSize);
        Assert.Equal(2, options.MaxConcurrency);
    }

    [Fact]
    public async Task Nunca_passa_do_limite_de_chamadas_em_paralelo()
    {
        int running = 0, peak = 0;
        var provider = new ScriptedProvider(async (b, _, ct) =>
        {
            var now = Interlocked.Increment(ref running);
            int seen;
            while ((seen = Volatile.Read(ref peak)) < now && Interlocked.CompareExchange(ref peak, now, seen) != seen) { }
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref running);
            return Ok(b);
        });
        var runner = new TranslationRunner(provider, NoWait(batchSize: 20, concurrency: 2));

        var run = await runner.RunAsync(Units(200), Build, ct: CancellationToken.None);

        Assert.Equal(200, run.Results.Count);
        Assert.Equal(2, peak);
    }

    [Fact]
    public async Task Repete_o_lote_que_falha_ate_dar_certo()
    {
        var provider = new ScriptedProvider((b, call, _) =>
            call < 3 ? throw new HttpRequestException("503") : Task.FromResult(Ok(b)));
        var runner = new TranslationRunner(provider, NoWait(retries: 3));

        var run = await runner.RunAsync(Units(25), Build, ct: CancellationToken.None);

        Assert.Equal(3, provider.Calls);
        Assert.Equal(25, run.Results.Count);
        Assert.Empty(run.Failures);
    }

    [Fact]
    public async Task Espera_com_backoff_exponencial_entre_tentativas()
    {
        var waits = new List<TimeSpan>();
        var provider = new ScriptedProvider((b, call, _) =>
            call < 4 ? throw new HttpRequestException("429") : Task.FromResult(Ok(b)));
        var options = new TranslationRunOptions
        {
            MaxRetries = 3,
            InitialBackoff = TimeSpan.FromSeconds(1),
            Delay = (t, _) => { lock (waits) waits.Add(t); return Task.CompletedTask; },
        };

        await new TranslationRunner(provider, options).RunAsync(Units(20), Build, ct: CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], waits);
    }

    [Fact]
    public async Task Lote_que_esgota_as_tentativas_vira_falha_e_os_outros_seguem()
    {
        var provider = new ScriptedProvider((b, _, _) =>
            b.Lines[0].Id == "u020" ? throw new HttpRequestException("500") : Task.FromResult(Ok(b)));
        var runner = new TranslationRunner(provider, NoWait(batchSize: 20, retries: 2));

        var run = await runner.RunAsync(Units(60), Build, ct: CancellationToken.None);

        Assert.Equal(40, run.Results.Count);
        var failure = Assert.Single(run.Failures);
        Assert.Equal(Enumerable.Range(20, 20).Select(n => $"u{n:D3}"), failure.LineIds);
        Assert.IsType<HttpRequestException>(failure.Error);
        Assert.Equal(3, failure.Attempts);
    }

    [Fact]
    public async Task Erro_de_programacao_nao_e_repetido()
    {
        var provider = new ScriptedProvider((_, _, _) => throw new ArgumentException("lote inválido"));
        var runner = new TranslationRunner(provider, NoWait(retries: 3));

        var run = await runner.RunAsync(Units(20), Build, ct: CancellationToken.None);

        Assert.Equal(1, provider.Calls);
        Assert.Single(run.Failures);
    }

    [Fact]
    public async Task Cancelar_no_meio_devolve_e_entrega_o_que_ja_voltou()
    {
        using var cts = new CancellationTokenSource();
        var delivered = new List<string>();
        var provider = new ScriptedProvider(async (b, call, ct) =>
        {
            if (call > 2)
            {
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
            }
            return Ok(b);
        });
        var runner = new TranslationRunner(provider, NoWait(batchSize: 20, concurrency: 1));

        var run = await runner.RunAsync(Units(100), Build,
            onBatchTranslated: results => { lock (delivered) delivered.AddRange(results.Select(r => r.Id)); },
            ct: cts.Token);

        Assert.True(run.Canceled);
        Assert.Equal(40, run.Results.Count);
        Assert.Equal(Enumerable.Range(0, 40).Select(n => $"u{n:D3}"), run.Results.Select(r => r.Id));
        Assert.Equal(run.Results.Select(r => r.Id).Order(), delivered.Order());
        Assert.Empty(run.Failures);
    }

    [Fact]
    public async Task Cancelar_durante_o_backoff_nao_conta_como_falha()
    {
        using var cts = new CancellationTokenSource();
        var provider = new ScriptedProvider((_, _, _) => throw new HttpRequestException("429"));
        var options = new TranslationRunOptions
        {
            Delay = (_, _) => { cts.Cancel(); return Task.FromCanceled(cts.Token); },
        };

        var run = await new TranslationRunner(provider, options).RunAsync(Units(20), Build, ct: cts.Token);

        Assert.True(run.Canceled);
        Assert.Empty(run.Failures);
        Assert.Empty(run.Results);
    }

    [Fact]
    public async Task Ja_cancelado_antes_de_comecar_nao_chama_o_provedor()
    {
        var provider = new FakeTranslationProvider();
        var run = await new TranslationRunner(provider, NoWait()).RunAsync(Units(30), Build, ct: new CancellationToken(true));

        Assert.True(run.Canceled);
        Assert.Empty(provider.ReceivedBatches);
    }

    [Fact]
    public async Task Informa_o_progresso_a_cada_lote()
    {
        var progress = new List<TranslationProgress>();
        var runner = new TranslationRunner(new FakeTranslationProvider(), NoWait(batchSize: 20, concurrency: 1));

        await runner.RunAsync(Units(40), Build, progress: new SyncProgress<TranslationProgress>(progress.Add), ct: CancellationToken.None);

        Assert.Equal([20, 40], progress.Select(p => p.CompletedLines));
        Assert.All(progress, p => Assert.Equal(40, p.TotalLines));
    }

    [Fact]
    public async Task Lista_vazia_nao_chama_o_provedor()
    {
        var provider = new FakeTranslationProvider();
        var run = await new TranslationRunner(provider, NoWait()).RunAsync([], Build, ct: CancellationToken.None);

        Assert.Empty(run.Results);
        Assert.Empty(provider.ReceivedBatches);
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
