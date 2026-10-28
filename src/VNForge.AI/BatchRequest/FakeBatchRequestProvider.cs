using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.BatchRequest;

/// <summary>
/// Provedor determinístico para testes e para o app sem chave: não usa rede nem SDK. Reescreve cada linha pela função dada e deixa de fora
/// as que ela devolve nulas ou iguais à tradução de agora.
/// </summary>
public sealed class FakeBatchRequestProvider(Func<TextUnit, string?> rewrite) : IBatchRequestProvider
{
    private readonly ConcurrentQueue<(string Request, TranslationBatch Batch)> _received = new();
    private int _calls;

    /// <summary>Os pedidos e lotes recebidos, na ordem das chamadas.</summary>
    public IReadOnlyCollection<(string Request, TranslationBatch Batch)> Received => _received;

    /// <summary>Número (a partir de 1) da chamada que lança <see cref="InvalidOperationException"/>; 0 desliga a falha.</summary>
    public int FailOnCall { get; init; }

    /// <summary>Troca a palavra inteira <paramref name="from"/> por <paramref name="to"/>, sem diferenciar maiúsculas.</summary>
    public static FakeBatchRequestProvider Replacing(string from, string to) =>
        new(unit => Regex.Replace(unit.Translation ?? string.Empty, $@"\b{Regex.Escape(from)}\b", to, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    public Task<IReadOnlyList<BatchRewrite>> RewriteAsync(string request, TranslationBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<BatchRewrite>>(ct);
        }

        _received.Enqueue((request, batch));
        if (Interlocked.Increment(ref _calls) == FailOnCall)
        {
            return Task.FromException<IReadOnlyList<BatchRewrite>>(new InvalidOperationException($"Falha simulada na chamada {FailOnCall}."));
        }

        IReadOnlyList<BatchRewrite> rewrites =
        [
            .. batch.Lines
                .Select(unit => (unit, text: rewrite(unit)))
                .Where(p => p.text is not null && !string.Equals(p.text, p.unit.Translation, StringComparison.Ordinal))
                .Select(p => new BatchRewrite(p.unit.Id, p.text!)),
        ];
        return Task.FromResult(rewrites);
    }
}
