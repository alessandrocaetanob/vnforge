using System.Collections.Concurrent;
using VNForge.Core.Text;

namespace VNForge.AI.Translation;

/// <summary>Como o <see cref="TranslationRunner"/> divide, paraleliza e repete o trabalho.</summary>
public sealed record TranslationRunOptions
{
    public const int MinBatchSize = 20;
    public const int MaxBatchSize = 50;

    /// <summary>Linhas por lote, de <see cref="MinBatchSize"/> a <see cref="MaxBatchSize"/>.</summary>
    public int BatchSize { get; init; } = 30;

    /// <summary>Chamadas ao provedor ao mesmo tempo.</summary>
    public int MaxConcurrency { get; init; } = 2;

    /// <summary>Tentativas extras depois da primeira, por lote.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Espera antes da primeira repetição; dobra a cada nova tentativa.</summary>
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Decide se um erro merece nova tentativa. Por padrão, tudo exceto erro de uso do código (<see cref="ArgumentException"/>, <see cref="NotSupportedException"/>).</summary>
    public Func<Exception, bool> IsRetryable { get; init; } = static ex => ex is not (ArgumentException or NotSupportedException);

    /// <summary>Espera entre tentativas; os testes trocam por uma que não espera.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = static (time, ct) => Task.Delay(time, ct);
}

/// <summary>Avanço da tradução: linhas já traduzidas do total.</summary>
public sealed record TranslationProgress(int CompletedLines, int TotalLines);

/// <summary>Um lote que não foi traduzido mesmo após as tentativas; suas linhas continuam sem tradução.</summary>
public sealed record TranslationBatchFailure(IReadOnlyList<string> LineIds, Exception Error, int Attempts);

/// <summary>Resultado de uma execução: o que voltou, o que falhou e se foi cancelada.</summary>
/// <param name="CallbackError">Erro lançado por <c>onBatchTranslated</c> ou pelo progresso; a execução para ali, e <paramref name="Results"/> mantém o que já voltou.</param>
/// <param name="Results">Traduções recebidas, na ordem das linhas de entrada; inclui as dos lotes concluídos antes de um cancelamento.</param>
public sealed record TranslationRunResult(
    IReadOnlyList<TranslationResult> Results,
    IReadOnlyList<TranslationBatchFailure> Failures,
    bool Canceled,
    Exception? CallbackError = null);

/// <summary>
/// Traduz uma lista de linhas em lotes, com chamadas em paralelo e retry com backoff exponencial. Cancelar não lança: a execução
/// devolve o que já voltou (e <see cref="TranslationRunResult.Canceled"/>), para o chamador gravar sem perder trabalho já pago.
/// </summary>
public sealed class TranslationRunner
{
    private readonly ITranslationProvider _provider;
    private readonly TranslationRunOptions _options;

    public TranslationRunner(ITranslationProvider provider, TranslationRunOptions? options = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options ?? new TranslationRunOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.BatchSize, TranslationRunOptions.MinBatchSize, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(_options.BatchSize, TranslationRunOptions.MaxBatchSize, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxConcurrency, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegative(_options.MaxRetries, nameof(options));
    }

    /// <param name="lines">Linhas a traduzir, na ordem do script.</param>
    /// <param name="buildBatch">Monta o lote (contexto, glossário) de um trecho de <paramref name="lines"/>.</param>
    /// <param name="onBatchTranslated">
    /// Chamado, um lote por vez (nunca em paralelo), assim que um lote volta; é o ponto para gravar a tradução no disco.
    /// </param>
    /// <param name="progress">Recebe o avanço a cada lote traduzido.</param>
    public async Task<TranslationRunResult> RunAsync(
        IReadOnlyList<TextUnit> lines,
        Func<IReadOnlyList<TextUnit>, TranslationBatch> buildBatch,
        Action<IReadOnlyList<TranslationResult>>? onBatchTranslated = null,
        IProgress<TranslationProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(buildBatch);

        var chunks = lines.Chunk(_options.BatchSize).Select((chunk, index) => (Index: index, Lines: (IReadOnlyList<TextUnit>)chunk)).ToList();
        var done = new ConcurrentDictionary<int, IReadOnlyList<TranslationResult>>();
        var failed = new ConcurrentDictionary<int, TranslationBatchFailure>();
        var gate = new object();
        var completedLines = 0;
        Exception? callbackError = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await Parallel.ForEachAsync(
                chunks,
                new ParallelOptions { MaxDegreeOfParallelism = _options.MaxConcurrency, CancellationToken = stop.Token },
                async (chunk, token) =>
                {
                    TranslationBatch batch;
                    try
                    {
                        batch = buildBatch(chunk.Lines);
                    }
                    catch (Exception ex)
                    {
                        failed[chunk.Index] = new TranslationBatchFailure([.. chunk.Lines.Select(l => l.Id)], ex, 0);
                        return;
                    }

                    var outcome = await TranslateWithRetryAsync(batch, token).ConfigureAwait(false);
                    if (outcome.Error is not null)
                    {
                        failed[chunk.Index] = new TranslationBatchFailure([.. chunk.Lines.Select(l => l.Id)], outcome.Error, outcome.Attempts);
                        return;
                    }
                    if (outcome.Results is null)
                        return;

                    done[chunk.Index] = outcome.Results;
                    lock (gate)
                    {
                        try
                        {
                            completedLines += outcome.Results.Count;
                            onBatchTranslated?.Invoke(outcome.Results);
                            progress?.Report(new TranslationProgress(completedLines, lines.Count));
                        }
                        catch (Exception ex)
                        {
                            callbackError ??= ex;
                            stop.Cancel();
                        }
                    }
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }

        return new TranslationRunResult(
            [.. done.OrderBy(p => p.Key).SelectMany(p => p.Value)],
            [.. failed.OrderBy(p => p.Key).Select(p => p.Value)],
            ct.IsCancellationRequested,
            callbackError);
    }

    private async Task<(IReadOnlyList<TranslationResult>? Results, Exception? Error, int Attempts)> TranslateWithRetryAsync(TranslationBatch batch, CancellationToken ct)
    {
        var attempts = 0;
        while (true)
        {
            attempts++;
            try
            {
                return (await _provider.TranslateAsync(batch, ct).ConfigureAwait(false), null, attempts);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return (null, null, attempts);
            }
            catch (Exception ex) when (attempts <= _options.MaxRetries && _options.IsRetryable(ex))
            {
                try
                {
                    await _options.Delay(_options.InitialBackoff * Math.Pow(2, attempts - 1), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return (null, null, attempts);
                }
            }
            catch (Exception ex)
            {
                return (null, ex, attempts);
            }
        }
    }
}
