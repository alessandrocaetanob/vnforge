using System.Collections.Concurrent;
using VNForge.Core.Engines;

namespace VNForge.Core.Images;

/// <summary>
/// O que o <see cref="ImageTextScanner"/> vai mandar ao modelo: as imagens do <see cref="ImageListing"/> menos as que o inventário anterior já
/// leu (pelo hash do conteúdo, onde quer que estejam), sem repetir a mesma imagem. Existe antes da execução para a CLI mostrar o custo estimado.
/// </summary>
public sealed class ImageScanPlan
{
    private ImageScanPlan(
        IReadOnlyList<ImageCandidate> candidates,
        IReadOnlyList<ImageCandidate> toRead,
        IReadOnlyDictionary<string, string> known,
        IReadOnlyDictionary<string, string> previous,
        IReadOnlyDictionary<string, string> previousTranslations)
    {
        Candidates = candidates;
        ToRead = toRead;
        Known = known;
        Previous = previous;
        PreviousTranslations = previousTranslations;
    }

    /// <summary>Todas as imagens do jogo que passaram no filtro.</summary>
    public IReadOnlyList<ImageCandidate> Candidates { get; }

    /// <summary>As que vão ao modelo: uma por conteúdo diferente, na ordem do levantamento.</summary>
    public IReadOnlyList<ImageCandidate> ToRead { get; }

    /// <summary>Quantas das <see cref="Candidates"/> já têm leitura e não vão ao modelo.</summary>
    public int Reused => Candidates.Count(c => Known.ContainsKey(c.Hash));

    internal IReadOnlyDictionary<string, string> Known { get; }

    internal IReadOnlyDictionary<string, string> Previous { get; }

    /// <summary>A tradução sugerida que o inventário anterior trazia, por hash; só as imagens que a tinham.</summary>
    internal IReadOnlyDictionary<string, string> PreviousTranslations { get; }

    /// <param name="existing">O <c>images.json</c> de antes.</param>
    /// <param name="force">Lê tudo de novo, mesmo o que o inventário já tem; a leitura antiga só fica para o que não chegar a ser relido.</param>
    /// <param name="limit">No máximo tantas leituras (chamadas ao modelo) nesta execução.</param>
    public static ImageScanPlan Build(ImageListing listing, IReadOnlyList<ImageTextEntry> existing, bool force = false, int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(existing);

        var previous = existing.GroupBy(e => e.Hash, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Text, StringComparer.Ordinal);
        var previousTranslations = existing.Where(e => !string.IsNullOrEmpty(e.Translation)).GroupBy(e => e.Hash, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Translation!, StringComparer.Ordinal);
        IReadOnlyDictionary<string, string> known = force ? new Dictionary<string, string>() : previous;

        var unique = listing.Candidates.Where(c => !known.ContainsKey(c.Hash)).DistinctBy(c => c.Hash, StringComparer.Ordinal);
        List<ImageCandidate> toRead = [.. limit is { } max ? unique.Take(Math.Max(0, max)) : unique];
        return new ImageScanPlan(listing.Candidates, toRead, known, previous, previousTranslations);
    }
}

/// <summary>Como o <see cref="ImageTextScanner"/> roda.</summary>
public sealed record ImageScanRun
{
    /// <summary>Leituras ao mesmo tempo.</summary>
    public int Concurrency { get; init; } = 4;
}

/// <summary>Andamento da leitura: quantas imagens (diferentes) já terminaram, de quantas.</summary>
public sealed record ImageScanProgress(int Done, int Total, string Name);

/// <summary>Uma imagem que o leitor não conseguiu ler (erro de rede, resposta fora do formato, pacote que mudou).</summary>
public sealed record ImageScanFailure(string Archive, string Path, string Message);

/// <summary>O resultado de <see cref="ImageTextScanner.ScanAsync"/>.</summary>
/// <param name="Entries">O inventário novo: uma entrada por imagem do jogo que tem leitura (a de agora, a do inventário anterior ou a de outra imagem de mesmo conteúdo), ordenada por pacote e caminho.</param>
/// <param name="Read">Imagens (diferentes) que foram ao modelo e voltaram lidas.</param>
/// <param name="Reused">Imagens que já tinham leitura no inventário e não foram ao modelo.</param>
/// <param name="Refused">Imagens que o modelo recusou ler; não entram no inventário e são tentadas de novo na próxima execução.</param>
public sealed record ImageScanResult(
    IReadOnlyList<ImageTextEntry> Entries,
    int Read,
    int Reused,
    IReadOnlyList<ImageScanFailure> Failed,
    IReadOnlyList<ImageCandidate> Refused,
    long InputTokens,
    long OutputTokens)
{
    /// <summary>A execução foi cancelada no meio; <see cref="Entries"/> traz o que já estava lido.</summary>
    public bool Cancelled { get; init; }

    /// <summary>Por que a execução parou antes do fim sem ser cancelada (falhas seguidas), em português; nulo quando foi até o fim.</summary>
    public string? AbortReason { get; init; }
}

/// <summary>
/// Lê, com um <see cref="IImageTextReader"/>, as imagens de um <see cref="ImageScanPlan"/> e monta o inventário. Abre cada pacote uma vez, lê as
/// imagens em sequência e deixa até <see cref="ImageScanRun.Concurrency"/> chamadas ao leitor em andamento. Uma imagem que falha não derruba as outras;
/// <see cref="MaxConsecutiveFailures"/> falhas seguidas param a execução, porque quase sempre é a chave ou a rede e cada tentativa custa tempo.
/// </summary>
public sealed class ImageTextScanner(IImageTextReader reader, IReadOnlyList<IArchiveFormat> formats)
{
    public const int MaxConsecutiveFailures = 5;

    public async Task<ImageScanResult> ScanAsync(
        string gameDirectory,
        ImageScanPlan plan,
        ImageScanRun? run = null,
        IProgress<ImageScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gameDirectory);
        ArgumentNullException.ThrowIfNull(plan);
        var state = new RunState(plan.ToRead.Count, progress);
        using var gate = new SemaphoreSlim(Math.Max(1, (run ?? new ImageScanRun()).Concurrency));
        List<Task> calls = [];

        foreach (var group in plan.ToRead.GroupBy(c => c.Archive, StringComparer.Ordinal))
        {
            if (state.Stopped || !await ReadArchiveAsync(gameDirectory, group.Key, [.. group], gate, calls, state, cancellationToken).ConfigureAwait(false))
            {
                break;
            }
        }

        await Task.WhenAll(calls).ConfigureAwait(false);
        return Build(plan, state);
    }

    /// <summary>O inventário de um plano que não tem nada a ler: só as leituras que o inventário anterior já trazia, sem abrir pacote nem chamar o leitor.</summary>
    public static ImageScanResult WithoutReading(ImageScanPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Build(plan, new RunState(0, null));
    }

    private static ImageScanResult Build(ImageScanPlan plan, RunState state)
    {
        var entries = plan.Candidates
            .Select(c => state.Readings.TryGetValue(c.Hash, out var text) || plan.Previous.TryGetValue(c.Hash, out text)
                ? new ImageTextEntry(c.Archive, c.Path, c.Hash, c.Info.Width, c.Info.Height, text, TranslationOf(plan, state, c.Hash, text))
                : null)
            .OfType<ImageTextEntry>()
            .OrderBy(e => e.Archive, StringComparer.Ordinal)
            .ThenBy(e => e.Path, StringComparer.Ordinal)
            .ToList();

        return new ImageScanResult(entries, state.Readings.Count, plan.Reused, [.. state.Failed], [.. state.Refused], state.InputTokens, state.OutputTokens)
        {
            Cancelled = state.Cancelled,
            AbortReason = state.AbortReason,
        };
    }

    // A sugestão da leitura de agora; sem ela (imagem sem texto ou leitor que não sugere), a do inventário anterior, se o texto lido é o mesmo.
    private static string? TranslationOf(ImageScanPlan plan, RunState state, string hash, string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        if (state.Translations.TryGetValue(hash, out var fresh) && fresh.Length > 0)
        {
            return fresh;
        }

        return plan.PreviousTranslations.TryGetValue(hash, out var previous) && string.Equals(plan.Previous[hash], text, StringComparison.Ordinal) ? previous : null;
    }

    /// <returns>Falso quando a execução foi cancelada e o resto dos pacotes não deve ser lido.</returns>
    private async Task<bool> ReadArchiveAsync(
        string gameDirectory, string archiveName, List<ImageCandidate> candidates, SemaphoreSlim gate, List<Task> calls, RunState state, CancellationToken ct)
    {
        IArchive archive;
        try
        {
            archive = ArchiveOpener.Open(Path.Combine(gameDirectory, archiveName.Replace('/', Path.DirectorySeparatorChar)), formats);
        }
        catch (Exception ex) when (ArchiveOpener.IsReadFailure(ex))
        {
            foreach (var candidate in candidates)
            {
                state.Fail(candidate, $"o pacote {archiveName} não pôde ser aberto: {ex.Message}");
            }

            return !state.Stopped;
        }

        using (archive)
        {
            var entries = archive.Entries.ToDictionary(e => e.Path, StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                try
                {
                    await gate.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    state.Cancelled = true;
                    return false;
                }

                if (state.Stopped)
                {
                    gate.Release();
                    return false;
                }

                byte[] data;
                try
                {
                    data = entries.TryGetValue(candidate.Path, out var entry)
                        ? ImageBytes.Read(archive, entry)
                        : throw new InvalidDataException("a imagem não está mais no pacote.");
                }
                catch (Exception ex) when (ArchiveOpener.IsReadFailure(ex))
                {
                    gate.Release();
                    state.Fail(candidate, ex.Message);
                    continue;
                }

                calls.Add(ReadOneAsync(candidate, data, gate, state, ct));
            }
        }

        return true;
    }

    private async Task ReadOneAsync(ImageCandidate candidate, byte[] data, SemaphoreSlim gate, RunState state, CancellationToken ct)
    {
        try
        {
            var image = new ImageToRead($"{candidate.Archive}/{candidate.Path}", candidate.Info.MediaType!, data);
            var reading = await reader.ReadAsync(image, ct).ConfigureAwait(false);
            state.Succeed(candidate, reading);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            state.Cancelled = true;
        }
        catch (ImageReadFailedException ex)
        {
            state.Fail(candidate, ex.Message, ex.InputTokens, ex.OutputTokens);
        }
        catch (Exception ex)
        {
            state.Fail(candidate, ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed class RunState(int total, IProgress<ImageScanProgress>? progress)
    {
        private int _done;
        private int _consecutiveFailures;
        private long _inputTokens;
        private long _outputTokens;
        private volatile string? _abortReason;
        private volatile bool _cancelled;

        public ConcurrentDictionary<string, string> Readings { get; } = new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, string> Translations { get; } = new(StringComparer.Ordinal);

        public ConcurrentQueue<ImageScanFailure> Failed { get; } = new();

        public ConcurrentQueue<ImageCandidate> Refused { get; } = new();

        public bool Cancelled
        {
            get => _cancelled;
            set => _cancelled = value;
        }

        public string? AbortReason => _abortReason;

        public bool Stopped => Cancelled || _abortReason is not null;

        public long InputTokens => Interlocked.Read(ref _inputTokens);

        public long OutputTokens => Interlocked.Read(ref _outputTokens);

        public void Succeed(ImageCandidate candidate, ImageTextReading reading)
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            Interlocked.Add(ref _inputTokens, reading.InputTokens);
            Interlocked.Add(ref _outputTokens, reading.OutputTokens);
            if (reading.Refused)
            {
                Refused.Enqueue(candidate);
            }
            else
            {
                Readings[candidate.Hash] = reading.Text.Trim();
                Translations[candidate.Hash] = reading.Translation.Trim();
            }

            Report(candidate);
        }

        public void Fail(ImageCandidate candidate, string message, long inputTokens = 0, long outputTokens = 0)
        {
            Interlocked.Add(ref _inputTokens, inputTokens);
            Interlocked.Add(ref _outputTokens, outputTokens);
            Failed.Enqueue(new ImageScanFailure(candidate.Archive, candidate.Path, message));
            if (Interlocked.Increment(ref _consecutiveFailures) >= MaxConsecutiveFailures)
            {
                _abortReason ??= $"As últimas {MaxConsecutiveFailures} imagens falharam em sequência (a última: {message}). A leitura parou; confira a chave de API e a conexão e rode de novo para continuar de onde parou.";
            }

            Report(candidate);
        }

        private void Report(ImageCandidate candidate) =>
            progress?.Report(new ImageScanProgress(Interlocked.Increment(ref _done), total, $"{candidate.Archive}/{candidate.Path}"));
    }
}
