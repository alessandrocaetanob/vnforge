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
    public async Task Lote_que_nao_consegue_ser_montado_vira_falha_sem_chamar_o_provedor()
    {
        var provider = new ScriptedProvider((b, _, _) => Task.FromResult(Ok(b)));
        var runner = new TranslationRunner(provider, NoWait(batchSize: 20));

        var run = await runner.RunAsync(Units(40), lines => lines[0].Id == "u020" ? throw new InvalidOperationException("sem glossário") : Build(lines), ct: CancellationToken.None);

        Assert.Equal(20, run.Results.Count);
        Assert.Equal(1, provider.Calls);
        var failure = Assert.Single(run.Failures);
        Assert.Equal(Enumerable.Range(20, 20).Select(n => $"u{n:D3}"), failure.LineIds);
        Assert.Equal(0, failure.Attempts);
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
    public async Task Informa_o_progresso_a_cada_lote()
    {
        var progress = new List<TranslationProgress>();
        var runner = new TranslationRunner(new FakeTranslationProvider(), NoWait(batchSize: 20, concurrency: 1));

        await runner.RunAsync(Units(40), Build, progress: new SyncProgress<TranslationProgress>(progress.Add), ct: CancellationToken.None);

        Assert.Equal([20, 40], progress.Select(p => p.CompletedLines));
        Assert.All(progress, p => Assert.Equal(40, p.TotalLines));
    }

    [Fact]
    public async Task Erro_ao_gravar_um_lote_para_a_execucao_sem_perder_o_que_voltou()
    {
        var runner = new TranslationRunner(new FakeTranslationProvider(), NoWait(batchSize: 20, concurrency: 1));

        var run = await runner.RunAsync(Units(100), Build,
            onBatchTranslated: _ => throw new IOException("disco cheio"),
            ct: CancellationToken.None);

        Assert.IsType<IOException>(run.CallbackError);
        Assert.False(run.Canceled);
        Assert.Equal(20, run.Results.Count);
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
