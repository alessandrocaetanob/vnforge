using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Beta.Messages;
using Anthropic.Models.Beta;
using Model = Anthropic.Models.Messages.Model;

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

        var response = await _client.Beta.Messages.Create(new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens,
            // Prompt caching: o sistema (estilo + glossário) se repete a cada lote.
            System = new List<BetaTextBlockParam>
            {
                new() { Text = request.System, CacheControl = new BetaCacheControlEphemeral() },
            },
            // Recusa de classificador: o servidor refaz o pedido no modelo de fallback na mesma chamada.
            Betas = SupportsServerFallback(request.Model) ? [AnthropicBeta.ServerSideFallback2026_06_01] : null,
            Fallbacks = SupportsServerFallback(request.Model) ? new BetaFallbacksParam(new List<BetaFallbackParam> { new(Model.ClaudeOpus4_8) }) : null,
            Thinking = new BetaThinkingConfigAdaptive(),
            OutputConfig = new BetaOutputConfig
            {
                Effort = ToSdk(request.Effort),
                Format = new BetaJsonOutputFormat { Schema = schemaDict },
            },
            Messages = [new BetaMessageParam(new BetaUserMessageParam { Content = request.User })],
        }, cancellationToken: ct).ConfigureAwait(false);

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
        var usage = new ClaudeUsage(
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.Usage.CacheReadInputTokens ?? 0,
            response.Usage.CacheCreationInputTokens ?? 0);
        return new ClaudeReply(ToWire(response.StopReason), text, usage);
    }

    /// <summary>
    /// O fallback no servidor existe para Opus e Fable; Haiku não tem (com <c>fallbacks</c> a API devolve 400) e o Sonnet
    /// só aceita a forma <c>"default"</c>, que o SDK ainda não expõe. Nesses a recusa chega ao provedor, que marca a linha.
    /// </summary>
    public static bool SupportsServerFallback(string model) => model is ClaudeModels.Opus or ClaudeModels.Fable;

    // ApiEnum.ToString() não garante o valor do fio; o provedor compara com "refusal" e "max_tokens".
    private static string? ToWire(ApiEnum<string, BetaStopReason>? stopReason) => stopReason?.Value() switch
    {
        BetaStopReason.Refusal => "refusal",
        BetaStopReason.MaxTokens => "max_tokens",
        BetaStopReason.EndTurn => "end_turn",
        BetaStopReason.StopSequence => "stop_sequence",
        BetaStopReason.ToolUse => "tool_use",
        BetaStopReason.PauseTurn => "pause_turn",
        BetaStopReason.ModelContextWindowExceeded => "model_context_window_exceeded",
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
