using System.Collections.Concurrent;
using VNForge.AI.Cost;
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

    /// <summary>Guarda de gasto: pergunta antes de um pedido acima do aviso e deixa de começar lotes quando o gasto chega ao limite. Sem ela, nada limita o gasto.</summary>
    public BudgetGuard? Budget { get; init; }
}

/// <summary>Avanço da tradução: linhas já traduzidas do total.</summary>
public sealed record TranslationProgress(int CompletedLines, int TotalLines);

/// <summary>Um lote que não foi traduzido mesmo após as tentativas; suas linhas continuam sem tradução.</summary>
public sealed record TranslationBatchFailure(IReadOnlyList<string> LineIds, Exception Error, int Attempts);

/// <summary>Resultado de uma execução: o que voltou, o que falhou e se foi cancelada.</summary>
/// <param name="CallbackError">Erro lançado por <c>onBatchTranslated</c> ou pelo progresso; a execução para ali, e <paramref name="Results"/> mantém o que já voltou.</param>
/// <param name="Results">Traduções recebidas, na ordem das linhas de entrada; inclui as dos lotes concluídos antes de um cancelamento.</param>
/// <param name="BudgetStop">Por que o <see cref="BudgetGuard"/> parou a execução; <see langword="null"/> quando ele não interveio. Os lotes que não começaram não são falhas.</param>
public sealed record TranslationRunResult(
    IReadOnlyList<TranslationResult> Results,
    IReadOnlyList<TranslationBatchFailure> Failures,
    bool Canceled,
    Exception? CallbackError = null,
    BudgetStop? BudgetStop = null);

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
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.BatchSize, TranslationRunOptions.MinBatchSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(_options.BatchSize, TranslationRunOptions.MaxBatchSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(_options.MaxRetries);
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

        if (await AuthorizeRequestAsync(lines, ct).ConfigureAwait(false) is { } early)
        {
            return early;
        }

        var chunks = lines.Chunk(_options.BatchSize).Select((chunk, index) => (Index: index, Lines: (IReadOnlyList<TextUnit>)chunk)).ToList();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var state = new RunState(lines.Count, buildBatch, onBatchTranslated, progress, stop);

        try
        {
            await Parallel.ForEachAsync(
                chunks,
                new ParallelOptions { MaxDegreeOfParallelism = _options.MaxConcurrency, CancellationToken = stop.Token },
                (chunk, token) => RunChunkWithBudgetAsync(state, chunk, token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // A parada veio de um erro no callback (guardado em callbackError): o resultado parcial é devolvido abaixo.
        }

        return state.ToResult(ct.IsCancellationRequested);
    }

    /// <summary>Resolve o pedido inteiro no <see cref="BudgetGuard"/> antes de começar; devolve o resultado vazio quando ele não deixa seguir.</summary>
    private async Task<TranslationRunResult?> AuthorizeRequestAsync(IReadOnlyList<TextUnit> lines, CancellationToken ct)
    {
        if (_options.Budget is not { } budget)
        {
            return null;
        }

        try
        {
            return await budget.AuthorizeAsync(budget.EstimateRequest(lines), ct).ConfigureAwait(false) is { } refusal
                ? new TranslationRunResult([], [], ct.IsCancellationRequested, BudgetStop: refusal)
                : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new TranslationRunResult([], [], Canceled: true);
        }
    }

    /// <summary>
    /// Reserva o custo estimado do lote enquanto ele roda: sem isso, as chamadas em paralelo passariam juntas pela checagem e o gasto ultrapassaria o
    /// limite por vários lotes. Os lotes que não cabem no que sobra do limite não começam.
    /// </summary>
    private async ValueTask RunChunkWithBudgetAsync(RunState state, (int Index, IReadOnlyList<TextUnit> Lines) chunk, CancellationToken token)
    {
        var budget = _options.Budget;
        var reserved = budget?.EstimateRequest(chunk.Lines) ?? 0m;
        if (budget is not null && !budget.TryReserve(reserved))
        {
            state.MarkLimitReached();
            return;
        }

        try
        {
            await RunChunkAsync(state, chunk, token).ConfigureAwait(false);
        }
        finally
        {
            budget?.Release(reserved);
        }
    }

    private async ValueTask RunChunkAsync(RunState state, (int Index, IReadOnlyList<TextUnit> Lines) chunk, CancellationToken token)
    {
        TranslationBatch batch;
        try
        {
            batch = state.BuildBatch(chunk.Lines);
        }
        catch (Exception ex)
        {
            state.Failed[chunk.Index] = new TranslationBatchFailure([.. chunk.Lines.Select(l => l.Id)], ex, 0);
            return;
        }

        var outcome = await TranslateWithRetryAsync(batch, token).ConfigureAwait(false);
        if (outcome.Error is not null)
        {
            state.Failed[chunk.Index] = new TranslationBatchFailure([.. chunk.Lines.Select(l => l.Id)], outcome.Error, outcome.Attempts);
        }
        else if (outcome.Results is null)
        {
            // Sem resultado nem erro: cancelamento, ou o limite chegou antes de uma nova tentativa.
            if (_options.Budget?.LimitReached == true && !token.IsCancellationRequested)
            {
                state.MarkLimitReached();
            }
        }
        else
        {
            state.Deliver(chunk.Index, outcome.Results);
        }
    }

    /// <summary>O que uma execução acumula enquanto os lotes rodam em paralelo.</summary>
    private sealed class RunState(
        int totalLines,
        Func<IReadOnlyList<TextUnit>, TranslationBatch> buildBatch,
        Action<IReadOnlyList<TranslationResult>>? onBatchTranslated,
        IProgress<TranslationProgress>? progress,
        CancellationTokenSource stop)
    {
        private readonly ConcurrentDictionary<int, IReadOnlyList<TranslationResult>> _done = new();
        private readonly object _gate = new();
        private int _completedLines;
        private Exception? _callbackError;
        private int _limitReached;

        public ConcurrentDictionary<int, TranslationBatchFailure> Failed { get; } = new();

        public TranslationBatch BuildBatch(IReadOnlyList<TextUnit> lines) => buildBatch(lines);

        public void MarkLimitReached() => Interlocked.Exchange(ref _limitReached, 1);

        /// <summary>Guarda o lote e chama o callback e o progresso, um lote por vez; um erro neles para a execução.</summary>
        public void Deliver(int index, IReadOnlyList<TranslationResult> results)
        {
            _done[index] = results;
            lock (_gate)
            {
                try
                {
                    _completedLines += results.Count;
                    onBatchTranslated?.Invoke(results);
                    progress?.Report(new TranslationProgress(_completedLines, totalLines));
                }
                catch (Exception ex)
                {
                    _callbackError ??= ex;
                    stop.Cancel();
                }
            }
        }

        public TranslationRunResult ToResult(bool canceled) => new(
            [.. _done.OrderBy(p => p.Key).SelectMany(p => p.Value)],
            [.. Failed.OrderBy(p => p.Key).Select(p => p.Value)],
            canceled,
            _callbackError,
            Volatile.Read(ref _limitReached) == 1 ? BudgetStop.LimitReached : null);
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
                    if (_options.Budget?.LimitReached == true)
                    {
                        return (null, null, attempts);
                    }
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
