using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Anthropic.Models.Beta.Messages.Batches;
using VNForge.AI.Claude;

namespace VNForge.AI.Batches;

/// <summary>
/// O cliente real da Message Batches API, sobre o SDK oficial (<c>client.Beta.Messages.Batches</c>, o mesmo espaço de nomes de <see cref="AnthropicMessageClient"/>).
/// Cada pedido leva o que a chamada de hoje leva: thinking adaptativo, <c>output_config.effort</c>, saída JSON pelo schema e o bloco estável do sistema com <c>cache_control</c>.
/// O fallback de recusa no servidor fica de fora: ele pede um cabeçalho beta por lote, e a linha recusada volta marcada como tal.
/// </summary>
public sealed class AnthropicMessageBatchClient(AnthropicClient? client = null) : IMessageBatchClient
{
    /// <summary>Limites da API por lote: 100 mil pedidos ou 256 MB (platform.claude.com, batch-processing).</summary>
    public const int MaxRequests = 100_000;

    public const long MaxBytes = 256L * 1024 * 1024;

    private readonly AnthropicClient _client = client ?? new AnthropicClient();

    public async Task<MessageBatchInfo> CreateAsync(IReadOnlyList<MessageBatchItem> items, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(items);
        var parameters = new BatchCreateParams { Requests = [.. items.Select(ToRequest)] };
        var batch = await _client.Beta.Messages.Batches.Create(parameters, cancellationToken: ct).ConfigureAwait(false);
        return ToInfo(batch);
    }

    public async Task<MessageBatchInfo> GetAsync(string batchId, CancellationToken ct) =>
        ToInfo(await _client.Beta.Messages.Batches.Retrieve(batchId, cancellationToken: ct).ConfigureAwait(false));

    public async Task<MessageBatchInfo> CancelAsync(string batchId, CancellationToken ct) =>
        ToInfo(await _client.Beta.Messages.Batches.Cancel(batchId, cancellationToken: ct).ConfigureAwait(false));

    public async IAsyncEnumerable<MessageBatchEntry> ResultsAsync(string batchId, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var response in _client.Beta.Messages.Batches.ResultsStreaming(batchId, cancellationToken: ct).ConfigureAwait(false))
        {
            yield return ToEntry(response);
        }
    }

    internal static Request ToRequest(MessageBatchItem item)
    {
        var request = item.Request;
        using var schema = JsonDocument.Parse(request.JsonSchema);
        var schemaDict = schema.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        return new Request
        {
            CustomID = item.CustomId,
            Params = new Params
            {
                Model = request.Model,
                MaxTokens = request.MaxTokens,
                System = AnthropicMessageClient.SystemBlocks(request),
                Thinking = new BetaThinkingConfigAdaptive(),
                OutputConfig = new BetaOutputConfig
                {
                    Effort = AnthropicMessageClient.ToSdk(request.Effort),
                    Format = new BetaJsonOutputFormat { Schema = schemaDict },
                },
                Messages = [new BetaMessageParam(AnthropicMessageClient.UserMessage(request))],
            },
        };
    }

    internal static MessageBatchInfo ToInfo(BetaMessageBatch batch)
    {
        var counts = batch.RequestCounts;
        var status = batch.ProcessingStatus.Value() switch
        {
            ProcessingStatus.Ended => MessageBatchStatus.Ended,
            ProcessingStatus.Canceling => MessageBatchStatus.Canceling,
            _ => MessageBatchStatus.InProgress,
        };
        return new MessageBatchInfo(batch.ID, status, (int)counts.Processing, (int)counts.Succeeded, (int)counts.Errored, (int)counts.Canceled, (int)counts.Expired);
    }

    internal static MessageBatchEntry ToEntry(BetaMessageBatchIndividualResponse response)
    {
        if (response.Result.TryPickSucceeded(out var succeeded))
        {
            var message = succeeded.Message;
            var text = string.Concat(message.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
            var usage = ClaudeUsage.ForRequest(
                message.Usage.InputTokens,
                message.Usage.OutputTokens,
                message.Usage.CacheReadInputTokens ?? 0,
                message.Usage.CacheCreationInputTokens ?? 0);
            return new MessageBatchEntry(response.CustomID, MessageBatchOutcome.Succeeded, new ClaudeReply(AnthropicMessageClient.ToWire(message.StopReason), text, usage));
        }

        if (response.Result.TryPickErrored(out var errored))
        {
            return new MessageBatchEntry(response.CustomID, MessageBatchOutcome.Errored, Error: errored.Error.ToString());
        }

        return new MessageBatchEntry(response.CustomID, response.Result.TryPickExpired(out _) ? MessageBatchOutcome.Expired : MessageBatchOutcome.Canceled);
    }
}
