using VNForge.AI.Claude;

namespace VNForge.AI.Batches;

/// <summary>Um pedido do lote: o <c>custom_id</c> com que o resultado volta e o pedido de mensagem de sempre.</summary>
public sealed record MessageBatchItem(string CustomId, ClaudeRequest Request);

public enum MessageBatchStatus
{
    /// <summary>A API ainda processa os pedidos.</summary>
    InProgress,

    /// <summary>O cancelamento foi pedido e a API ainda o conclui.</summary>
    Canceling,

    /// <summary>Todos os pedidos têm resultado; ele fica guardado por 29 dias.</summary>
    Ended,
}

/// <summary>O estado do lote na API. A API informa o andamento em contagem de pedidos, não de linhas.</summary>
public sealed record MessageBatchInfo(string Id, MessageBatchStatus Status, int Processing, int Succeeded, int Errored, int Canceled, int Expired)
{
    public int Total => Processing + Succeeded + Errored + Canceled + Expired;

    /// <summary>Pedidos que já têm resultado.</summary>
    public int Done => Succeeded + Errored + Canceled + Expired;

    /// <summary>De 0 a 1; 0 quando o lote ainda não contou nenhum pedido.</summary>
    public double Fraction => Total == 0 ? 0 : (double)Done / Total;
}

public enum MessageBatchOutcome
{
    Succeeded,
    Errored,
    Canceled,
    Expired,
}

/// <summary>O resultado de um pedido do lote.</summary>
/// <param name="Reply">A resposta do modelo; nula quando o pedido não teve sucesso.</param>
/// <param name="Error">O que a API disse do pedido que falhou; nulo quando teve sucesso.</param>
public sealed record MessageBatchEntry(string CustomId, MessageBatchOutcome Outcome, ClaudeReply? Reply = null, string? Error = null);

/// <summary>
/// A fronteira com a Message Batches API (<c>/v1/messages/batches</c>). O cliente real usa o SDK <c>Anthropic</c>; os testes passam o
/// <see cref="FakeMessageBatchClient"/>.
/// </summary>
public interface IMessageBatchClient
{
    /// <summary>Envia os pedidos num lote só (até 100 mil pedidos ou 256 MB) e devolve o lote criado.</summary>
    Task<MessageBatchInfo> CreateAsync(IReadOnlyList<MessageBatchItem> items, CancellationToken ct);

    Task<MessageBatchInfo> GetAsync(string batchId, CancellationToken ct);

    /// <summary>Pede o cancelamento; os pedidos já processados continuam com resultado.</summary>
    Task<MessageBatchInfo> CancelAsync(string batchId, CancellationToken ct);

    /// <summary>Os resultados de um lote encerrado, na ordem em que a API os entrega (que não é a dos pedidos).</summary>
    IAsyncEnumerable<MessageBatchEntry> ResultsAsync(string batchId, CancellationToken ct);
}
