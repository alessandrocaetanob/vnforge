using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core.Bible;
using VNForge.Core.Projects;

namespace VNForge.AI.Bible;

/// <summary>Como o <see cref="BibleRunner"/> divide e repete o trabalho.</summary>
public sealed record BibleRunOptions
{
    public const int DefaultScenesPerCall = 12;

    /// <summary>Cenas por chamada. As chamadas de um capítulo leem o mesmo roteiro, que o cache de prompt serve depois da primeira.</summary>
    public int ScenesPerCall { get; init; } = DefaultScenesPerCall;

    /// <summary>Tentativas extras depois da primeira, por chamada.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Espera antes da primeira repetição; dobra a cada nova tentativa.</summary>
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Espera entre tentativas; os testes trocam por uma que não espera.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = static (time, ct) => Task.Delay(time, ct);

    /// <summary>
    /// Uma recusa do modelo repetida dá a mesma recusa, e uma resposta cortada pelo limite de tokens de saída é cortada no mesmo ponto com o mesmo limite: nenhuma das duas
    /// se repete. O resto, exceto erro de uso do código, sim.
    /// </summary>
    public Func<Exception, bool> IsRetryable { get; init; } = static ex => ex is not (ArgumentException or NotSupportedException or ClaudeRefusalException or ClaudeResponseException { HitMaxTokens: true });

    /// <summary>Guarda de gasto: antes de cada capítulo, o gasto no limite encerra a leitura. O aviso do valor do pedido inteiro é do chamador (<see cref="BudgetGuard.AuthorizeAsync"/>).</summary>
    public BudgetGuard? Budget { get; init; }

    /// <summary>O modelo, para o registro de como a bíblia foi gerada.</summary>
    public string Model { get; init; } = ClaudeModels.Opus;

    /// <summary>O que a leitura gastou até agora, em dólar, para o registro da geração; sem ela o registro diz zero.</summary>
    public Func<decimal?>? RunDollars { get; init; }

    public TimeProvider Clock { get; init; } = TimeProvider.System;
}

/// <summary>Andamento da leitura: capítulos e cenas já escritas do total.</summary>
public sealed record BibleProgress(int ChaptersDone, int ChaptersTotal, int ScenesDone, int ScenesTotal, string Chapter);

/// <summary>Um capítulo (ou a sinopse) que não foi lido mesmo após as tentativas; o que já tinha sido escrito dele fica na bíblia.</summary>
public sealed record BibleChapterFailure(string Chapter, Exception Error, int Attempts);

/// <param name="ScenesWritten">Cenas escritas na bíblia nesta leitura.</param>
/// <param name="ChaptersRead">Capítulos lidos até o fim.</param>
/// <param name="OverviewWritten">A sinopse e a linha do tempo foram escritas.</param>
/// <param name="BudgetStop">Por que o <see cref="BudgetGuard"/> parou a leitura; nulo quando ele não interveio.</param>
public sealed record BibleRunResult(
    int ScenesWritten, int ChaptersRead, IReadOnlyList<BibleChapterFailure> Failures, bool Canceled, BudgetStop? BudgetStop = null, bool OverviewWritten = false);

/// <summary>
/// Gera a bíblia do jogo: lê um capítulo por vez, em chamadas de algumas cenas, e grava cada cena no <c>bible.json</c> assim que volta, para uma falha ou um cancelamento
/// não perder o que já foi pago. O que a pessoa escreveu nunca é trocado. Igual no app e no <c>vnf bible</c>.
/// </summary>
public sealed class BibleRunner
{
    private readonly IBibleProvider _provider;
    private readonly BibleRunOptions _options;

    public BibleRunner(IBibleProvider provider, BibleRunOptions? options = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options ?? new BibleRunOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.ScenesPerCall, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(_options.MaxRetries);
    }

    /// <param name="includeOverview">Escreve a sinopse e a linha do tempo no fim; só faz sentido quando <paramref name="chapters"/> é o jogo inteiro.</param>
    public async Task<BibleRunResult> RunAsync(
        ProjectFolder project,
        IReadOnlyList<BibleChapterInput> chapters,
        IProgress<BibleProgress>? progress,
        CancellationToken ct,
        bool includeOverview = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(chapters);

        // Um bible.json ilegível para aqui, antes de gastar e antes de KeepPreviousBible copiá-lo por cima do backup bom.
        project.LoadBible();
        project.KeepPreviousBible();
        var state = new RunState(chapters.Count, chapters.Sum(c => c.Scenes.Count), progress);

        try
        {
            foreach (var chapter in chapters)
            {
                ct.ThrowIfCancellationRequested();
                if (_options.Budget is { LimitReached: true })
                {
                    state.Stop = BudgetStop.LimitReached;
                    break;
                }

                await ReadChapterAsync(project, chapter, state, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            state.Canceled = true;
        }

        var overview = false;
        if (includeOverview && !state.Canceled && state.Stop is null && state.Failures.Count == 0 && chapters.Count > 0)
        {
            overview = await WriteOverviewAsync(project, chapters, state.Failures, ct).ConfigureAwait(false);
        }

        if (state.Written > 0 || overview)
        {
            var generation = new BibleGeneration(_options.Clock.GetUtcNow(), _options.Model, state.Written, state.Read, _options.RunDollars?.Invoke() ?? 0m);
            project.UpdateBible(bible => bible with { Generation = generation });
        }

        return new BibleRunResult(state.Written, state.Read, state.Failures, state.Canceled || ct.IsCancellationRequested, state.Stop, overview);
    }

    /// <summary>Lê um capítulo em chamadas de algumas cenas e grava cada uma; a primeira chamada que falha depois das tentativas encerra o capítulo.</summary>
    private async Task ReadChapterAsync(ProjectFolder project, BibleChapterInput chapter, RunState state, CancellationToken ct)
    {
        foreach (var keys in chapter.Scenes.Select(s => s.Key).Chunk(_options.ScenesPerCall))
        {
            ct.ThrowIfCancellationRequested();
            var (scenes, error, attempts) = await DescribeWithRetryAsync(chapter, keys, ct).ConfigureAwait(false);
            if (scenes is null)
            {
                state.Failures.Add(new BibleChapterFailure(chapter.Name, error!, attempts));
                return;
            }

            project.UpdateBible(bible => scenes.Aggregate(bible, (current, scene) => current.WithGeneratedScene(scene)));
            state.Written += scenes.Count;
            state.Report(chapter.Name);
        }

        state.Read++;
        state.Report(chapter.Name);
    }

    private sealed class RunState(int chaptersTotal, int scenesTotal, IProgress<BibleProgress>? progress)
    {
        public List<BibleChapterFailure> Failures { get; } = [];

        public int Written { get; set; }

        public int Read { get; set; }

        public BudgetStop? Stop { get; set; }

        public bool Canceled { get; set; }

        public void Report(string chapter) => progress?.Report(new BibleProgress(Read, chaptersTotal, Written, scenesTotal, chapter));
    }

    /// <summary>
    /// "Reescrever com a IA" num campo: lê o capítulo da cena de novo e troca só <paramref name="field"/>, mesmo que a pessoa o tenha escrito (ela pediu).
    /// O campo volta a ser da IA.
    /// </summary>
    public async Task<BibleScene> RewriteFieldAsync(ProjectFolder project, BibleChapterInput chapter, string sceneKey, BibleField field, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(chapter);
        project.LoadBible();
        var scenes = await _provider.DescribeScenesAsync(chapter, [sceneKey], ct).ConfigureAwait(false);
        var generated = scenes.Single(s => s.Key == sceneKey);
        return project.UpdateBible(bible => bible.WithScene((bible.Scene(sceneKey) ?? new BibleScene(sceneKey)).WithGeneratedField(generated, field))).Scene(sceneKey)!;
    }

    private async Task<bool> WriteOverviewAsync(ProjectFolder project, IReadOnlyList<BibleChapterInput> chapters, List<BibleChapterFailure> failures, CancellationToken ct)
    {
        var bible = project.LoadBible();
        IReadOnlyList<BibleChapterDigest> digests =
        [
            .. chapters
                .Select(c => new BibleChapterDigest(c.Name, [.. c.Scenes.Select(s => bible.Scene(s.Key)).OfType<BibleScene>().Where(s => s.Summary.Length > 0).Select(s => s.Summary)]))
                .Where(d => d.Summaries.Count > 0),
        ];
        if (digests.Count == 0)
        {
            return false;
        }

        var attempts = 0;
        while (true)
        {
            attempts++;
            try
            {
                var overview = await _provider.SummarizeGameAsync(digests, ct).ConfigureAwait(false);
                project.UpdateBible(b => b.WithGeneratedOverview(overview.Synopsis, overview.Timeline));
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex) when (attempts <= _options.MaxRetries && _options.IsRetryable(ex))
            {
                await WaitAsync(attempts, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failures.Add(new BibleChapterFailure("Sinopse e linha do tempo", ex, attempts));
                return false;
            }
        }
    }

    private async Task<(IReadOnlyList<BibleScene>? Scenes, Exception? Error, int Attempts)> DescribeWithRetryAsync(
        BibleChapterInput chapter, IReadOnlyList<string> keys, CancellationToken ct)
    {
        var attempts = 0;
        while (true)
        {
            attempts++;
            try
            {
                return (await _provider.DescribeScenesAsync(chapter, keys, ct).ConfigureAwait(false), null, attempts);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempts <= _options.MaxRetries && _options.IsRetryable(ex))
            {
                await WaitAsync(attempts, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (null, ex, attempts);
            }
        }
    }

    private Task WaitAsync(int attempts, CancellationToken ct) => _options.Delay(_options.InitialBackoff * Math.Pow(2, attempts - 1), ct);
}
