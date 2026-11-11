using System.Runtime.CompilerServices;
using System.Text.Json;
using VNForge.AI.Claude;

namespace VNForge.AI.Batches;

/// <summary>
/// A Message Batches API de mentira, para os testes: guarda os lotes na memória e só os encerra quando o teste manda (<see cref="Finish"/>),
/// como a API real, que leva até 24 h. O resultado de cada pedido sai de <c>reply</c>; sem ele, um JSON de tradução com <c>[PT] </c> antes do original.
/// </summary>
public sealed class FakeMessageBatchClient : IMessageBatchClient
{
    private readonly Dictionary<string, FakeBatch> batches = new(StringComparer.Ordinal);
    private int counter;

    /// <summary>Dá a resposta do modelo a um pedido; o padrão traduz cada linha do pedido.</summary>
    public Func<MessageBatchItem, ClaudeReply> Reply { get; set; } = DefaultReply;

    /// <summary>Quais pedidos terminam com erro, expirados ou cancelados, em vez de sucesso; o padrão é nenhum.</summary>
    public Func<MessageBatchItem, MessageBatchOutcome> OutcomeOf { get; set; } = static _ => MessageBatchOutcome.Succeeded;

    /// <summary>Faz o próximo <see cref="CreateAsync"/> falhar com esta exceção.</summary>
    public Exception? CreateFails { get; set; }

    /// <summary>Faz toda consulta ao lote falhar com esta exceção, como uma rede fora do ar.</summary>
    public Exception? GetFails { get; set; }

    /// <summary>Faz todo cancelamento falhar com esta exceção.</summary>
    public Exception? CancelFails { get; set; }

    /// <summary>Roda depois de cada consulta que deu certo; o teste o usa para saber que o app já perguntou.</summary>
    public Action? AfterGet { get; set; }

    /// <summary>Quantas vezes cada operação foi chamada.</summary>
    public int Creates { get; private set; }

    public int Gets { get; private set; }

    public int Cancels { get; private set; }

    /// <summary>Os pedidos do último lote enviado.</summary>
    public IReadOnlyList<MessageBatchItem> LastItems { get; private set; } = [];

    public Task<MessageBatchInfo> CreateAsync(IReadOnlyList<MessageBatchItem> items, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (CreateFails is { } failure)
        {
            CreateFails = null;
            throw failure;
        }

        Creates++;
        LastItems = items;
        var id = $"msgbatch_fake{++counter:D4}";
        var batch = new FakeBatch(id, [.. items]);
        batches[id] = batch;
        return Task.FromResult(batch.Info());
    }

    public Task<MessageBatchInfo> GetAsync(string batchId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Gets++;
        if (GetFails is { } failure)
        {
            throw failure;
        }

        var info = Find(batchId).Info();
        AfterGet?.Invoke();
        return Task.FromResult(info);
    }

    public Task<MessageBatchInfo> CancelAsync(string batchId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Cancels++;
        if (CancelFails is { } failure)
        {
            throw failure;
        }

        var batch = Find(batchId);
        batch.CancelRequested = true;
        return Task.FromResult(batch.Info());
    }

    /// <summary>A API termina o lote: os pedidos viram o resultado que <see cref="OutcomeOf"/> e <see cref="Reply"/> dão. Um lote cancelado antes termina com os pedidos cancelados.</summary>
    public void Finish(string batchId)
    {
        var batch = Find(batchId);
        batch.Ended = true;
    }

    public async IAsyncEnumerable<MessageBatchEntry> ResultsAsync(string batchId, [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var batch = Find(batchId);
        if (!batch.Ended)
        {
            throw new InvalidOperationException("O lote ainda não terminou.");
        }

        // A API devolve os resultados fora de ordem; o fake inverte para provar que quem lê usa o custom_id.
        foreach (var item in Enumerable.Reverse(batch.Items))
        {
            yield return batch.Entry(item, this);
            await Task.Yield();
        }
    }

    private FakeBatch Find(string batchId) =>
        batches.TryGetValue(batchId, out var batch) ? batch : throw new KeyNotFoundException($"Lote desconhecido: {batchId}.");

    private static ClaudeReply DefaultReply(MessageBatchItem item)
    {
        // O pedido termina com "Traduza estas linhas:" e o array JSON das linhas, uma por objeto com id e text.
        const string Marker = "Traduza estas linhas:";
        var json = item.Request.User[(item.Request.User.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..];
        using var lines = JsonDocument.Parse(json);
        var translations = lines.RootElement.EnumerateArray()
            .Select(line => new { id = line.GetProperty("id").GetString(), translation = "[PT] " + line.GetProperty("text").GetString() });
        return new ClaudeReply("end_turn", JsonSerializer.Serialize(new { translations }), new ClaudeUsage(1000, 500, 0, 0));
    }

    private sealed class FakeBatch(string id, List<MessageBatchItem> items)
    {
        public string Id { get; } = id;

        public List<MessageBatchItem> Items { get; } = items;

        public bool Ended { get; set; }

        public bool CancelRequested { get; set; }

        public MessageBatchInfo Info()
        {
            if (Ended)
            {
                return new MessageBatchInfo(Id, MessageBatchStatus.Ended, 0, Items.Count, 0, 0, 0);
            }

            return new MessageBatchInfo(Id, CancelRequested ? MessageBatchStatus.Canceling : MessageBatchStatus.InProgress, Items.Count, 0, 0, 0, 0);
        }

        public MessageBatchEntry Entry(MessageBatchItem item, FakeMessageBatchClient owner)
        {
            var outcome = CancelRequested ? MessageBatchOutcome.Canceled : owner.OutcomeOf(item);
            if (outcome == MessageBatchOutcome.Succeeded)
            {
                return new MessageBatchEntry(item.CustomId, outcome, owner.Reply(item));
            }

            return new MessageBatchEntry(item.CustomId, outcome, Error: outcome == MessageBatchOutcome.Errored ? "overloaded_error" : null);
        }
    }
}
