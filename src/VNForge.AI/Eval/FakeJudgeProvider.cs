using System.Collections.Concurrent;
using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Eval;

/// <summary>Juiz determinístico para testes e para o app sem chave: sem rede e sem SDK. Por padrão dá uma nota entre 5 e 9 que depende só do texto avaliado.</summary>
public sealed class FakeJudgeProvider(Func<TextUnit, double>? score = null) : IJudgeProvider
{
    private readonly Func<TextUnit, double> _score = score ?? (unit => 5 + (unit.Translation ?? string.Empty).Length % 5);
    private readonly ConcurrentQueue<TranslationBatch> _received = new();

    /// <summary>Lotes recebidos, na ordem das chamadas.</summary>
    public IReadOnlyCollection<TranslationBatch> ReceivedBatches => _received;

    /// <summary>Tokens que o juiz diz ter gasto a cada chamada; nulo não gasta nada.</summary>
    public ClaudeUsage? UsagePerCall { get; init; }

    private readonly Lock _gate = new();
    private ClaudeUsage? _usage;

    public ClaudeUsage? Usage
    {
        get { lock (_gate) return _usage; }
    }

    public Task<IReadOnlyList<JudgeScore>> JudgeAsync(TranslationBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<JudgeScore>>(ct);
        }

        _received.Enqueue(batch);
        if (UsagePerCall is { } perCall)
        {
            lock (_gate)
            {
                _usage = _usage is null ? perCall : _usage + perCall;
            }
        }

        IReadOnlyList<JudgeScore> scores = [.. batch.Lines.Select(u => new JudgeScore(u.Id, _score(u)))];
        return Task.FromResult(scores);
    }
}
