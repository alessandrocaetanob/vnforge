using System.Text;
using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Review;

/// <summary>
/// Provedor de revisão sobre o Claude. Pede, por linha, uma sugestão curta ou vazio quando a tradução está boa, em JSON por
/// <c>output_config.format</c>, e confere que a resposta traz exatamente os ids do lote. Retry fica no <see cref="TranslationRunner"/>.
/// </summary>
public sealed class ClaudeReviewProvider(IClaudeMessageClient client, ClaudeOptions? options = null) : IReviewProvider
{
    private readonly ClaudeOptions _options = options ?? new ClaudeOptions();

    private static readonly JsonSerializerOptions PromptJson = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal const string Schema = """
        {"type":"object","properties":{"reviews":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"suggestion":{"type":"string"}},"required":["id","suggestion"],"additionalProperties":false}}},"required":["reviews"],"additionalProperties":false}
        """;

    private readonly Lock _usageLock = new();
    private ClaudeUsage _usage = new(0, 0, 0, 0);

    /// <summary>Tokens acumulados de todas as chamadas deste provedor, recusas incluídas.</summary>
    public ClaudeUsage Usage
    {
        get { lock (_usageLock) return _usage; }
    }

    /// <summary>Revisa o lote. Uma recusa refaz o lote linha a linha, e só as linhas recusadas voltam com <see cref="ReviewResult.Refused"/>.</summary>
    public Task<IReadOnlyList<ReviewResult>> ReviewAsync(TranslationBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ct.ThrowIfCancellationRequested();
        return ClaudePromptParts.WithRefusalFallback(
            batch,
            b => ReviewOnceAsync(b, ct),
            line => new ReviewResult(line.Id, string.Empty, Refused: true));
    }

    private async Task<IReadOnlyList<ReviewResult>> ReviewOnceAsync(TranslationBatch batch, CancellationToken ct)
    {
        var request = new ClaudeRequest(_options.Model, _options.Effort, _options.MaxTokens, BuildSystem(batch), BuildUser(batch), Schema);
        var reply = await client.CompleteAsync(request, ct).ConfigureAwait(false);

        if (reply.Usage is { } usage)
            lock (_usageLock)
                _usage += usage;

        if (reply.StopReason == "refusal")
            throw new ClaudeRefusalException("O modelo recusou revisar este lote (stop_reason: refusal).");
        if (reply.StopReason == "max_tokens")
            throw new ClaudeResponseException("A resposta foi cortada por limite de tokens; reduza o lote.");

        return Parse(reply.Text, batch);
    }

    private static string BuildSystem(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Você revisa traduções de visual novels do japonês para o português do Brasil.");
        sb.AppendLine("Para cada linha, confira sentido, naturalidade, tom do personagem, glossário e marcadores de controle.");
        sb.AppendLine("Devolva uma sugestão curta (uma ou duas frases, em português) por linha, com o mesmo id; se a tradução está boa, devolva a sugestão vazia.");
        sb.AppendLine("Não reescreva a tradução inteira nem altere os marcadores. Responda apenas com o JSON pedido.");
        ClaudePromptParts.AppendContext(sb, batch, "Glossário (termos obrigatórios):");
        return sb.ToString();
    }

    private static string BuildUser(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        ClaudePromptParts.AppendPrevious(sb, batch, "Contexto anterior (não revisar):");
        sb.AppendLine("Revise estas linhas:");
        sb.AppendLine(JsonSerializer.Serialize(
            batch.Lines.Select(u => new { id = u.Id, kind = u.Kind.ToString(), speaker = u.Speaker, text = u.Source, translation = u.Translation }),
            PromptJson));
        return sb.ToString();
    }

    private static IReadOnlyList<ReviewResult> Parse(string text, TranslationBatch batch)
    {
        var suggestions = ClaudePromptParts.ParseValues(text, "reviews", "suggestion", batch);
        return [.. batch.Lines.Select((u, i) => new ReviewResult(u.Id, suggestions[i].Trim()))];
    }
}
