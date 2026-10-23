using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;

namespace VNForge.AI.Claude;

/// <summary>
/// Cliente real sobre o SDK oficial. Lê a chave de <c>ANTHROPIC_API_KEY</c> (ou do cliente injetado); a chave nunca
/// passa por arquivo do projeto.
/// </summary>
public sealed class AnthropicMessageClient(AnthropicClient? client = null) : IClaudeMessageClient
{
    private readonly AnthropicClient _client = client ?? new AnthropicClient();

    public async Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var schema = JsonDocument.Parse(request.JsonSchema);
        var schemaDict = schema.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens,
            // Prompt caching: o sistema (estilo + glossário) se repete a cada lote.
            System = new List<TextBlockParam>
            {
                new() { Text = request.System, CacheControl = new CacheControlEphemeral() },
            },
            Thinking = new ThinkingConfigAdaptive(),
            OutputConfig = new OutputConfig
            {
                Effort = ToSdk(request.Effort),
                Format = new JsonOutputFormat { Schema = schemaDict },
            },
            Messages = [new MessageParam(new UserMessageParam { Content = request.User })],
        }, cancellationToken: ct).ConfigureAwait(false);

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        return new ClaudeReply(ToWire(response.StopReason), text);
    }

    // ApiEnum.ToString() não garante o valor do fio; o provedor compara com "refusal" e "max_tokens".
    private static string? ToWire(ApiEnum<string, StopReason>? stopReason) => stopReason?.Value() switch
    {
        StopReason.Refusal => "refusal",
        StopReason.MaxTokens => "max_tokens",
        StopReason.EndTurn => "end_turn",
        StopReason.StopSequence => "stop_sequence",
        StopReason.ToolUse => "tool_use",
        StopReason.PauseTurn => "pause_turn",
        StopReason.ModelContextWindowExceeded => "model_context_window_exceeded",
        _ => null,
    };

    private static Effort ToSdk(ClaudeEffort effort) => effort switch
    {
        ClaudeEffort.Low => Effort.Low,
        ClaudeEffort.Medium => Effort.Medium,
        ClaudeEffort.High => Effort.High,
        ClaudeEffort.XHigh => Effort.Xhigh,
        ClaudeEffort.Max => Effort.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(effort)),
    };
}
