using System.Text;
using System.Text.Json;
using VNForge.AI.Translation;

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

    public async Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ct.ThrowIfCancellationRequested();
        if (batch.Lines.Count == 0)
            return [];

        var request = new ClaudeRequest(_options.Model, _options.Effort, _options.MaxTokens, BuildSystem(batch), BuildUser(batch), Schema);
        var reply = await client.CompleteAsync(request, ct).ConfigureAwait(false);

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

        if (!string.IsNullOrWhiteSpace(batch.StyleGuide))
            sb.AppendLine().AppendLine("Guia de estilo:").AppendLine(batch.StyleGuide.Trim());

        if (batch.Glossary.Count > 0)
        {
            sb.AppendLine().AppendLine("Glossário (use sempre estes termos):");
            foreach (var g in batch.Glossary)
                sb.AppendLine(string.IsNullOrWhiteSpace(g.Notes) ? $"- {g.Source} => {g.Target}" : $"- {g.Source} => {g.Target} ({g.Notes})");
        }

        if (batch.Characters.Count > 0)
        {
            sb.AppendLine().AppendLine("Personagens:");
            foreach (var c in batch.Characters)
                {
                var details = new List<string>();
                if (c.Gender is not null) details.Add($"gênero: {c.Gender}");
                if (c.Tone is not null) details.Add($"tom: {c.Tone}");
                sb.AppendLine(details.Count == 0 ? $"- {c.Source} => {c.Target}" : $"- {c.Source} => {c.Target} ({string.Join("; ", details)})");
            }
        }

        return sb.ToString();
    }

    private static string BuildUser(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        if (batch.PreviousLines.Count > 0)
        {
            sb.AppendLine("Contexto anterior (não traduzir):");
            foreach (var u in batch.PreviousLines)
                sb.AppendLine($"{(u.Speaker is null ? string.Empty : u.Speaker + ": ")}{u.Source}");
            sb.AppendLine();
        }

        sb.AppendLine("Traduza estas linhas:");
        sb.AppendLine(JsonSerializer.Serialize(
            batch.Lines.Select(u => new { id = u.Id, kind = u.Kind.ToString(), speaker = u.Speaker, text = u.Source }),
            PromptJson));
        return sb.ToString();
    }

    private static IReadOnlyList<TranslationResult> Parse(string text, TranslationBatch batch)
    {
        Dictionary<string, string> byId = new(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(text);
            foreach (var item in doc.RootElement.GetProperty("translations").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? throw new ClaudeResponseException("Resposta com id nulo.");
                if (!byId.TryAdd(id, item.GetProperty("translation").GetString() ?? string.Empty))
                    throw new ClaudeResponseException($"Resposta com id repetido: '{id}'.");
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ClaudeResponseException("A resposta do Claude não é o JSON esperado.", e);
        }

        var expected = batch.Lines.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var extra = byId.Keys.Where(k => !expected.Contains(k)).ToList();
        var missing = expected.Where(k => !byId.ContainsKey(k)).ToList();
        if (extra.Count > 0 || missing.Count > 0)
            throw new ClaudeResponseException($"Ids da resposta não batem com o lote. Faltam: [{string.Join(", ", missing)}]; sobram: [{string.Join(", ", extra)}].");

        return [.. batch.Lines.Select(u => new TranslationResult(u.Id, byId[u.Id]))];
    }
}
