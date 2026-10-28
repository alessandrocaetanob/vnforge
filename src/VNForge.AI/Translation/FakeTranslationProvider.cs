using System.Collections.Concurrent;
using VNForge.Core.Text;

namespace VNForge.AI.Translation;

/// <summary>
/// Provedor determinístico para testes e para o app sem chave: não usa rede nem SDK. Por padrão traduz <c>X</c> como
/// <c>[PT] X</c>, o que mantém os marcadores de controle e deixa a linha visivelmente "traduzida".
/// </summary>
public sealed class FakeTranslationProvider : ITranslationProvider, IRewriteProvider
{
    private readonly ConcurrentQueue<(TranslationBatch Batch, RewriteRequest Request)> _rewrites = new();

    private readonly Func<TextUnit, string> _translate;
    private readonly ConcurrentQueue<TranslationBatch> _received = new();
    private int _calls;

    public FakeTranslationProvider(Func<TextUnit, string>? translate = null)
    {
        _translate = translate ?? (unit => $"[PT] {unit.Source}");
    }

    /// <summary>Lotes recebidos, na ordem das chamadas.</summary>
    public IReadOnlyCollection<TranslationBatch> ReceivedBatches => _received;

    /// <summary>Número (a partir de 1) da chamada que lança <see cref="InvalidOperationException"/>; 0 desliga a falha.</summary>
    public int FailOnCall { get; init; }

    public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<TranslationResult>>(ct);

        _received.Enqueue(batch);
        if (Interlocked.Increment(ref _calls) == FailOnCall)
            return Task.FromException<IReadOnlyList<TranslationResult>>(new InvalidOperationException($"Falha simulada na chamada {FailOnCall}."));

        IReadOnlyList<TranslationResult> results = [.. batch.Lines.Select(u => new TranslationResult(u.Id, _translate(u)))];
        return Task.FromResult(results);
    }

    /// <summary>Pedidos de reescrita recebidos, na ordem das chamadas.</summary>
    public IReadOnlyCollection<(TranslationBatch Batch, RewriteRequest Request)> ReceivedRewrites => _rewrites;

    /// <summary>Devolve duas opções determinísticas, <c>[Direção 1] tradução</c> e <c>[Direção 2] tradução</c>, que mantêm os marcadores da tradução de hoje.</summary>
    public Task<RewriteResult> RewriteAsync(TranslationBatch batch, RewriteRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(request);
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<RewriteResult>(ct);

        _rewrites.Enqueue((batch, request));
        return Task.FromResult(new RewriteResult(batch.Lines[0].Id, [$"[{request.Direction} 1] {request.Current}", $"[{request.Direction} 2] {request.Current}"]));
    }
}
