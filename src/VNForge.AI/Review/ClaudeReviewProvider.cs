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
    public async Task<IReadOnlyList<ReviewResult>> ReviewAsync(TranslationBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ct.ThrowIfCancellationRequested();
        if (batch.Lines.Count == 0)
            return [];

        try
        {
            return await ReviewOnceAsync(batch, ct).ConfigureAwait(false);
        }
        catch (ClaudeRefusalException)
        {
            if (batch.Lines.Count == 1)
                return [new ReviewResult(batch.Lines[0].Id, string.Empty, Refused: true)];
        }

        List<ReviewResult> results = [];
        foreach (var line in batch.Lines)
            results.AddRange(await ReviewAsync(SingleLine(batch, line), ct).ConfigureAwait(false));
        return results;
    }

    private static TranslationBatch SingleLine(TranslationBatch batch, TextUnit line) => new([line])
    {
        PreviousLines = batch.PreviousLines,
        Glossary = batch.Glossary,
        Characters = batch.Characters,
        StyleGuide = batch.StyleGuide,
    };

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

        if (!string.IsNullOrWhiteSpace(batch.StyleGuide))
            sb.AppendLine().AppendLine("Guia de estilo:").AppendLine(batch.StyleGuide.Trim());

        AppendGlossary(sb, batch.Glossary);
        AppendCharacters(sb, batch.Characters);

        return sb.ToString();
    }

    private static void AppendGlossary(StringBuilder sb, IReadOnlyList<GlossaryEntry> glossary)
    {
        if (glossary.Count == 0)
            return;

        sb.AppendLine().AppendLine("Glossário (termos obrigatórios):");
        foreach (var g in glossary)
            sb.AppendLine(string.IsNullOrWhiteSpace(g.Notes) ? $"- {g.Source} => {g.Target}" : $"- {g.Source} => {g.Target} ({g.Notes})");
    }

    private static void AppendCharacters(StringBuilder sb, IReadOnlyList<CharacterInfo> characters)
    {
        if (characters.Count == 0)
            return;

        sb.AppendLine().AppendLine("Personagens:");
        foreach (var c in characters)
        {
            var details = new List<string>();
            if (c.Gender is not null) details.Add($"gênero: {c.Gender}");
            if (c.Tone is not null) details.Add($"tom: {c.Tone}");
            sb.AppendLine(details.Count == 0 ? $"- {c.Source} => {c.Target}" : $"- {c.Source} => {c.Target} ({string.Join("; ", details)})");
        }
    }

    private static string BuildUser(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        if (batch.PreviousLines.Count > 0)
        {
            sb.AppendLine("Contexto anterior (não revisar):");
            foreach (var u in batch.PreviousLines)
                sb.AppendLine($"{(u.Speaker is null ? string.Empty : u.Speaker + ": ")}{u.Source}");
            sb.AppendLine();
        }

        sb.AppendLine("Revise estas linhas:");
        sb.AppendLine(JsonSerializer.Serialize(
            batch.Lines.Select(u => new { id = u.Id, kind = u.Kind.ToString(), speaker = u.Speaker, text = u.Source, translation = u.Translation }),
            PromptJson));
        return sb.ToString();
    }

    private static IReadOnlyList<ReviewResult> Parse(string text, TranslationBatch batch)
    {
        Dictionary<string, string> byId = new(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(text);
            foreach (var item in doc.RootElement.GetProperty("reviews").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? throw new ClaudeResponseException("Resposta com id nulo.");
                if (!byId.TryAdd(id, item.GetProperty("suggestion").GetString() ?? string.Empty))
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

        return [.. batch.Lines.Select(u => new ReviewResult(u.Id, byId[u.Id].Trim()))];
    }
}
