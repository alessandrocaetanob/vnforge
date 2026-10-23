using System.Text;
using System.Text.Json;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Claude;

/// <summary>
/// Provedor de tradução sobre o Claude. Monta o pedido, exige saída JSON por <c>output_config.format</c> e confere que
/// a resposta traz exatamente os ids do lote. Retry, reenvio de linhas ruins e fallback de recusa ficam acima dele.
/// </summary>
public sealed class ClaudeTranslationProvider(IClaudeMessageClient client, ClaudeOptions? options = null) : ITranslationProvider
{
    private readonly ClaudeOptions _options = options ?? new ClaudeOptions();

    // Sem escape de não-ASCII: o japonês chega legível ao modelo e gasta menos tokens.
    private static readonly JsonSerializerOptions PromptJson = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal const string Schema = """
        {"type":"object","properties":{"translations":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"translation":{"type":"string"}},"required":["id","translation"],"additionalProperties":false}}},"required":["translations"],"additionalProperties":false}
        """;

    private readonly Lock _usageLock = new();
    private ClaudeUsage _usage = new(0, 0, 0, 0);

    /// <summary>Tokens acumulados de todas as chamadas deste provedor, recusas incluídas, com os lidos do cache.</summary>
    public ClaudeUsage Usage
    {
        get { lock (_usageLock) return _usage; }
    }

    /// <summary>
    /// Traduz o lote. Uma recusa que sobrevive ao fallback do servidor não derruba o lote: ele é refeito linha a linha
    /// e só as linhas recusadas voltam com <see cref="TranslationResult.Refused"/>.
    /// </summary>
    public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ct.ThrowIfCancellationRequested();
        return ClaudePromptParts.WithRefusalFallback(
            batch,
            b => TranslateOnceAsync(b, ct),
            line => new TranslationResult(line.Id, string.Empty, Refused: true));
    }

    private async Task<IReadOnlyList<TranslationResult>> TranslateOnceAsync(TranslationBatch batch, CancellationToken ct)
    {
        var request = new ClaudeRequest(_options.Model, _options.Effort, _options.MaxTokens, BuildSystem(batch), BuildUser(batch), Schema);
        var reply = await client.CompleteAsync(request, ct).ConfigureAwait(false);

        if (reply.Usage is { } usage)
            lock (_usageLock)
                _usage += usage;

        if (reply.StopReason == "refusal")
            throw new ClaudeRefusalException("O modelo recusou traduzir este lote (stop_reason: refusal).");
        if (reply.StopReason == "max_tokens")
            throw new ClaudeResponseException("A resposta foi cortada por limite de tokens; reduza o lote.");

        return Parse(reply.Text, batch);
    }

    private static string BuildSystem(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Você traduz linhas de visual novels do japonês para o português do Brasil.");
        sb.AppendLine("Mantenha exatamente os marcadores de controle do original e devolva uma tradução por linha, com o mesmo id.");
        sb.AppendLine("Responda apenas com o JSON pedido.");
        ClaudePromptParts.AppendContext(sb, batch, "Glossário (use sempre estes termos):");
        return sb.ToString();
    }

    private static string BuildUser(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        ClaudePromptParts.AppendPrevious(sb, batch, "Contexto anterior (não traduzir):");
        sb.AppendLine("Traduza estas linhas:");
        sb.AppendLine(JsonSerializer.Serialize(
            batch.Lines.Select(u => new { id = u.Id, kind = u.Kind.ToString(), speaker = u.Speaker, text = u.Source }),
            PromptJson));
        return sb.ToString();
    }

    private static IReadOnlyList<TranslationResult> Parse(string text, TranslationBatch batch)
    {
        var translations = ClaudePromptParts.ParseValues(text, "translations", "translation", batch);
        return [.. batch.Lines.Select((u, i) => new TranslationResult(u.Id, translations[i]))];
    }
}
